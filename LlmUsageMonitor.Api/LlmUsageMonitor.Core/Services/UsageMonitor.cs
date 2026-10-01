using System.Globalization;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

public sealed class UsageMonitor(
	IEnumerable<IUsageReader> readers,
	IProviderLocks locks,
	IProviderStateRepository states,
	IUsageSnapshotRepository snapshots,
	IResetRepository resets,
	ISettingsService settingsService,
	IHealthTracker health,
	IClaudeKeepAlive keepAlive,
	IAutomaticTrigger automaticTrigger,
	INotificationService notifications,
	IJobScheduler scheduler,
	TimeProvider time,
	ILogger<UsageMonitor> logger) : IUsageMonitor
{
	private readonly Dictionary<Provider, IUsageReader> _readers = readers.ToDictionary(reader => reader.Provider);

	public async Task Poll(Provider provider, CancellationToken cancellationToken)
	{
		using var providerLock = await locks.TryAcquire(provider, ProviderLocks.PollWait, cancellationToken);
		if (providerLock is null)
		{
			logger.LogWarning("{Provider} reading skipped: another CLI process still holds the provider after {Wait}", provider, ProviderLocks.PollWait);
			return;
		}

		var now = time.GetUtcNow();
		var state = await states.Get(provider, cancellationToken);
		if (state.BackoffUntil is { } backoffUntil && backoffUntil > now)
		{
			logger.LogInformation("{Provider} reading skipped: rate limited until {BackoffUntil}", provider, backoffUntil);
			return;
		}

		var settings = await settingsService.Get(cancellationToken);
		string? cycleKey;
		try
		{
			if (provider == Provider.Claude)
			{
				state = keepAlive.ScheduleNext(state, await keepAlive.EnsureFresh(cancellationToken));
			}

			var windows = await _readers[provider].Read(cancellationToken);
			if (windows.Count == 0)
			{
				throw new ProviderException(ProviderErrorCodes.NoUsageData, "The provider returned no percentage-based usage windows.");
			}

			// The refresh and the reading may take a while: the reading is dated when it is received.
			var readAt = time.GetUtcNow();
			var reading = new UsageReading(readAt, windows);
			await snapshots.Add(provider, reading, cancellationToken);
			await RecordResets(provider, state.LastReading, reading, cancellationToken);

			var triggerWindow = reading.TriggerWindow;
			var lastReset = triggerWindow is null ? null : await resets.GetLast(provider, triggerWindow.Id, cancellationToken);
			cycleKey = UsageRules.CycleKey(state, reading, lastReset, readAt);

			state = await health.RecordSuccess(state, readAt, cancellationToken) with
			{
				LastReading = reading,
				LastSuccessAt = readAt,
				CurrentCycleKey = cycleKey
			};
			state = SchedulePostResetCheck(state, reading, settings.Triggers.For(provider).AutoEnabled, readAt);
			await states.Save(state, cancellationToken);
		}
		// Any failure counts, not only the provider ones (storage, CLI process): the health, the alerts and the state follow.
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			var failure = exception as ProviderException;
			if (failure is null)
			{
				logger.LogError(exception, "{Provider} reading failed unexpectedly", provider);
				failure = new(ProviderErrorCodes.Unexpected, exception.Message, exception);
			}
			else
			{
				logger.LogWarning("{Provider} reading failed: {Code} {Message}", provider, failure.Code, failure.Message);
			}

			state = await health.RecordFailure(state, failure, time.GetUtcNow(), settings.Notifications.ReadFailureThreshold, cancellationToken);
			await states.Save(state, cancellationToken);
			return;
		}

		if (cycleKey is { } && settings.Triggers.For(provider).AutoEnabled)
			// Still under the provider lock: the prompt never overlaps a reading.
		{
			await automaticTrigger.Run(provider, cycleKey, cancellationToken);
		}
	}

	/// <summary>
	///     A reset is stored once per transition from the previous reading: a poll that stops before saving its state finds the
	///     same transition again, which the repository recognizes and neither stores nor notifies twice.
	/// </summary>
	private async Task RecordResets(Provider provider, UsageReading? previous, UsageReading current, CancellationToken cancellationToken)
	{
		foreach (var (before, after) in UsageRules.DetectResets(previous, current))
		{
			var reset = await resets.TryAdd(provider, after.Id, previous!.FetchedAt, current.FetchedAt, before.UsedPercent, after.UsedPercent, before.ResetsAt, cancellationToken);
			if (reset is null)
			{
				continue;
			}

			var detail = string.Create(CultureInfo.InvariantCulture, $"{after.Id} : {before.UsedPercent:0.#} % → {after.UsedPercent:0.#} % consommé");
			await notifications.Notify(NotificationKind.Reset, provider, detail, cancellationToken);
		}
	}

	/// <summary>
	///     A reading that sees a reset time ahead on the trigger window schedules a check one minute after it.
	/// </summary>
	private ProviderState SchedulePostResetCheck(ProviderState state, UsageReading reading, bool autoEnabled, DateTimeOffset now)
	{
		// A check whose time has come is running (this very poll, maybe) or done: it is forgotten, never deleted, since deleting
		// the running job would abort it.
		if (state.PendingResetCheck is { } due && due.RunAt <= now)
		{
			state = state with { PendingResetCheck = null };
		}

		if (!autoEnabled || reading.TriggerWindow?.ResetsAt is not { } resetsAt || resetsAt <= now)
		{
			return state;
		}

		var runAt = resetsAt.AddMinutes(1);
		// The provider may shift the announced reset by a few seconds between readings.
		if (state.PendingResetCheck is { } pending && Math.Abs((pending.RunAt - runAt).TotalSeconds) < 60)
		{
			return state;
		}

		if (state.PendingResetCheck is { } previous)
		{
			scheduler.Delete(previous.JobId);
		}

		return state with { PendingResetCheck = new(scheduler.SchedulePostResetCheck(state.Provider, runAt), runAt) };
	}
}