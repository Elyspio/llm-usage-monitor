using Elyspio.Utils.Results;
using Elyspio.Utils.Telemetry;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

/// <summary>Earned resets, serialized with polling and prompts. Each logical redemption is stored before contacting the provider.</summary>
public sealed class ResetCreditService(IEnumerable<IUsageReader> readers, IEnumerable<IResetCreditConsumer> consumers,
	IResetCreditRunRepository runs, IProviderStateRepository states, IUsageSnapshotRepository snapshots, IResetRepository resets,
	IProviderLocks locks, ISettingsService settings, IClaudeKeepAlive keepAlive, INotificationService notifications,
	IJobScheduler scheduler, TimeProvider time, ILogger<ResetCreditService> logger) : IResetCreditService
{
	private readonly Dictionary<Provider, IUsageReader> _readers = readers.ToDictionary(reader => reader.Provider);
	private readonly Dictionary<Provider, IResetCreditConsumer> _consumers = consumers.ToDictionary(consumer => consumer.Provider);

	public async Task<Result<ResetCreditRun>> ConsumeManual(Provider provider, ConsumeResetCreditRequest request, CancellationToken cancellationToken)
	{
		using var trace = logger.Trace($"{provider} {Log.F(request.CreditId)}");

		if (!Enum.IsDefined(provider) || string.IsNullOrWhiteSpace(request.CreditId) || request.CreditId.Length > 200 || !Guid.TryParse(request.IdempotencyKey, out var key))
			return trace.Fail(new RequestInvalid("request", "A known provider, a credit ID and a UUID idempotency key are required."));
		if (!_consumers.ContainsKey(provider))
			return trace.Fail(new RequestInvalid("provider", $"{provider.Label()} has no earned resets."));
		using var providerLock = await locks.TryAcquire(provider, ProviderLocks.PollWait, cancellationToken);
		if (providerLock is null) return trace.Fail(new ProviderError(ProviderErrorCode.CliBusy, "Another operation is running for this provider."));
		var id = key.ToString();
		if (await runs.Get(id, cancellationToken) is { } existing)
		{
			if (existing.Provider != provider || existing.CreditId != request.CreditId)
				return trace.Fail(new RequestInvalid("idempotencyKey", "This key belongs to another redemption."));
			if (existing.Status == ResetCreditRunStatus.Running && !existing.Manual)
			{
				// Explicit manual confirmation resumes the persisted request, preserving HTTP replay semantics.
				existing = existing with { Manual = true };
				await runs.Save(existing, cancellationToken);
				return existing.NextRetryAt > time.GetUtcNow() ? existing : await Execute(existing, cancellationToken);
			}
			return existing;
		}
		// A lost HTTP reply must not authorize a second logical redemption of the same grant.
		if ((await runs.GetPending(provider, cancellationToken)).Any(run => run.CreditId == request.CreditId))
			return trace.Fail(new ProviderError(ProviderErrorCode.CliBusy, "A reset request already exists for this credit. Refresh the dashboard to resume the same request."));
		var state = await states.Get(provider, cancellationToken);
		if (provider == Provider.Claude && !(await keepAlive.EnsureFresh(state, cancellationToken)).TryGet(out state, out var keepAliveError)) return trace.Fail(keepAliveError);
		if (!(await Refresh(state, cancellationToken)).TryGet(out state, out var refreshError)) return trace.Fail(refreshError);
		var credit = state.ResetCredits?.Credits?.FirstOrDefault(credit => credit.Id == request.CreditId);
		if (credit is null || !ResetCreditRules.Available(credit, time.GetUtcNow()))
			return trace.Fail(new RequestInvalid("creditId", "This credit is unavailable, expired or not currently eligible."));
		var run = await runs.Start(new(id, provider, credit.Id, true, null, time.GetUtcNow(), credit.ExpiresAt,
			ResetCreditRunStatus.Running, Before: state.LastReading?.Windows), cancellationToken);
		return await Execute(run, cancellationToken);
	}

	public async Task<bool> ProcessAutomatic(Provider provider, CancellationToken cancellationToken)
	{
		if (!_consumers.ContainsKey(provider)) return false;
		var configuration = (await settings.Get(cancellationToken)).ResetCredits.For(provider);
		var attempted = false;
		foreach (var pending in await runs.GetPending(provider, cancellationToken))
		{
			if (pending.ExpiresAt <= time.GetUtcNow())
			{
				await Complete(pending, new(false, "expiredOrUnconfirmed"), cancellationToken);
				continue;
			}
			if ((!pending.Manual && !configuration.AutoEnabled) || pending.NextRetryAt > time.GetUtcNow()) continue;
			await Execute(pending, cancellationToken);
			attempted = true;
		}
		if (!configuration.AutoEnabled) return attempted;
		var state = await states.Get(provider, cancellationToken);
		var now = time.GetUtcNow();
		// Snapshot the grants: refreshed remaining uses bound the loop; unknown or undated details never authorize automatic use.
		var due = ResetCreditRules.Ordered((state.ResetCredits?.Credits ?? []).Where(credit => ResetCreditRules.Available(credit, now)
			&& credit.ExpiresAt <= now.AddMinutes(configuration.BeforeExpiryMinutes))).ToList();
		foreach (var original in due)
		{
			for (var index = 0; index < original.RemainingUses; index++)
			{
				configuration = (await settings.Get(cancellationToken)).ResetCredits.For(provider);
				if (!configuration.AutoEnabled) return attempted;
				state = await states.Get(provider, cancellationToken);
				var credit = state.ResetCredits?.Credits?.FirstOrDefault(credit => credit.Id == original.Id);
				if (credit is null || !ResetCreditRules.Available(credit, time.GetUtcNow()) || credit.ExpiresAt > time.GetUtcNow().AddMinutes(configuration.BeforeExpiryMinutes)) break;
				if ((await runs.GetPending(provider, cancellationToken)).Any(run => run.CreditId == credit.Id)) break;
				var automaticKey = ResetCreditRules.AutomaticKey(provider, credit);
				var run = await runs.Start(new(Guid.NewGuid().ToString(), provider, credit.Id, false, automaticKey,
					time.GetUtcNow(), credit.ExpiresAt, ResetCreditRunStatus.Running, Before: state.LastReading?.Windows), cancellationToken);
				if (run.Status != ResetCreditRunStatus.Running) break;
				var completed = await Execute(run, cancellationToken);
				attempted = true;
				if (completed.Status != ResetCreditRunStatus.Succeeded) break;
			}
		}
		return attempted;
	}

	private async Task<ResetCreditRun> Execute(ResetCreditRun run, CancellationToken cancellationToken)
	{
		if (run.Attempts >= TriggerService.MaxAttempts || (run.Attempts > 0 && run.ExpiresAt <= time.GetUtcNow()))
			return await Complete(run, new(false, "expiredOrUnconfirmed"), cancellationToken);
		run = run with { Attempts = run.Attempts + 1, NextRetryAt = null };
		await runs.Save(run, cancellationToken);
		var state = await states.Get(run.Provider, cancellationToken);
		var previouslySuppressed = state.CreditResetSuppressesTrigger;
		// Persist before I/O as well: a reset can succeed while the service loses the reply or restarts.
		await states.Save(state with { CreditResetSuppressesTrigger = true, CreditResetRefreshPending = true, CurrentCycleKey = null }, cancellationToken);
		ResetCreditResult result;
		try
		{
			var consumed = await _consumers[run.Provider].Consume(run.CreditId, run.Id, cancellationToken);
			if (consumed.TryGet(out var value, out var error)) result = value;
			else
			{
				logger.LogWarning("{Provider} reset credit attempt {Attempt} failed: {Code} {Message}", run.Provider, run.Attempts, error.ProviderCode, error.Message);
				result = FailedAttempt(error.ProviderCode);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
		catch (Exception exception)
		{
			logger.LogWarning(exception, "{Provider} reset credit attempt {Attempt} failed", run.Provider, run.Attempts);
			result = FailedAttempt(ProviderErrorCode.UnexpectedError);
		}
		// Caller cancellation leaves the running record intact: startup polling resumes the same idempotency key.
		try
		{
			var refreshed = await Refresh(await states.Get(run.Provider, cancellationToken), cancellationToken);
			if (refreshed.TryGet(out var refreshedState, out var refreshError)) run = run with { After = refreshedState.LastReading?.Windows };
			else await ForgetResetCredits(run.Provider, null, refreshError.Message, cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			await ForgetResetCredits(run.Provider, exception, exception.Message, cancellationToken);
		}
		if (!result.Succeeded && !result.Retryable && !previouslySuppressed)
		{
			state = await states.Get(run.Provider, cancellationToken);
			await states.Save(state with { CreditResetSuppressesTrigger = false, CreditResetRefreshPending = false }, cancellationToken);
		}
		return await Complete(run, result, cancellationToken);
	}

	private async Task<ResetCreditRun> Complete(ResetCreditRun run, ResetCreditResult result, CancellationToken cancellationToken)
	{
		var now = time.GetUtcNow();
		DateTimeOffset? retryAt = null;
		if (result.Retryable && run.Attempts < TriggerService.MaxAttempts)
		{
			var candidate = result.RetryAt ?? now + TriggerService.RetryDelays[Math.Max(0, run.Attempts - 1)];
			if (candidate > now && (run.ExpiresAt is null || candidate < run.ExpiresAt)) retryAt = candidate;
		}
		run = run with
		{
			Status = result.Succeeded ? ResetCreditRunStatus.Succeeded : retryAt is { } ? ResetCreditRunStatus.Running
				: result.Retryable || result.Outcome == "expiredOrUnconfirmed" ? ResetCreditRunStatus.Failed : ResetCreditRunStatus.Refused,
			EndedAt = retryAt is null ? now : null,
			NextRetryAt = retryAt,
			Outcome = result.Outcome
		};
		await runs.Save(run, cancellationToken);
		if (retryAt is { } retry) scheduler.ScheduleTriggerRetry(run.Provider, retry);
		else
		{
			var before = string.Join(", ", (run.Before ?? []).Select(window => $"{window.Id}: {window.UsedPercent:0.#}%"));
			var after = string.Join(", ", (run.After ?? []).Select(window => $"{window.Id}: {window.UsedPercent:0.#}%"));
			await notifications.Notify(result.Succeeded ? NotificationKind.ResetCreditSucceeded : NotificationKind.ResetCreditFailed,
				run.Provider, $"Reset {(run.Manual ? "manuel" : "avant expiration")} : {result.Outcome}. Usage avant : {before}. Après : {after}.", cancellationToken);
		}
		return run;
	}

	private static ResetCreditResult FailedAttempt(ProviderErrorCode code)
	{
		var retryable = code is not (ProviderErrorCode.AuthExpired or ProviderErrorCode.AuthRequired or ProviderErrorCode.CredentialsUnavailable or ProviderErrorCode.CliUnsupportedOption or ProviderErrorCode.AccessDenied);
		return new(false, code.ToStoredCode(), retryable);
	}

	private async Task ForgetResetCredits(Provider provider, Exception? exception, string reason, CancellationToken cancellationToken)
	{
		logger.LogWarning(exception, "{Provider} quotas could not be refreshed after the reset attempt: {Reason}", provider, reason);
		await states.Save((await states.Get(provider, cancellationToken)) with { ResetCredits = null }, cancellationToken);
	}

	private async Task<Result<ProviderState, ProviderError>> Refresh(ProviderState state, CancellationToken cancellationToken)
	{
		var read = await _readers[state.Provider].ReadAccount(cancellationToken);
		if (!read.TryGet(out var account, out var error)) return error;
		if (account.Windows.Count == 0) return new ProviderError(ProviderErrorCode.NoUsageData, "No usage windows returned after the reset.");
		var reading = new UsageReading(time.GetUtcNow(), account.Windows);
		await snapshots.Add(state.Provider, reading, cancellationToken);
		foreach (var (before, after) in UsageRules.DetectResets(state.LastReading, reading))
			await resets.TryAdd(state.Provider, after.Id, state.LastReading!.FetchedAt, reading.FetchedAt, before.UsedPercent, after.UsedPercent, before.ResetsAt, cancellationToken);
		state = state with
		{
			LastReading = reading,
			ResetCredits = account.ResetCredits,
			LastSuccessAt = reading.FetchedAt,
			CreditResetWindowResetsAt = state.CreditResetRefreshPending ? reading.TriggerWindow?.ResetsAt : state.CreditResetWindowResetsAt,
			CreditResetRefreshPending = false
		};
		await states.Save(state, cancellationToken);
		return state;
	}
}
