using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     The automatic trigger, started by a reading that holds the provider lock.
/// </summary>
public interface IAutomaticTrigger
{
	/// <summary>
	///     Runs the prompt for the cycle; does nothing when the cycle already had its trigger, unless a transient failure left
	///     a retry that is now due.
	/// </summary>
	Task Run(Provider provider, string cycleKey, CancellationToken cancellationToken);
}

public sealed class TriggerService(
	IEnumerable<IPromptRunner> runners,
	ITriggerRunRepository runs,
	IProviderLocks locks,
	ISettingsService settingsService,
	INotificationService notifications,
	IJobScheduler scheduler,
	TimeProvider time,
	ILogger<TriggerService> logger) : ITriggerService, IAutomaticTrigger
{
	/// <summary>
	///     Delays before each retry of an automatic run. A retry is a reading scheduled when it is due: it prompts again only
	///     while the cycle still waits for its first message, and neither holds the provider lock nor a worker meanwhile.
	/// </summary>
	public static readonly IReadOnlyList<TimeSpan> RetryDelays = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];

	public static readonly int MaxAttempts = RetryDelays.Count + 1;

	private readonly Dictionary<Provider, SemaphoreSlim> _requestGates = Enum.GetValues<Provider>().ToDictionary(provider => provider, _ => new SemaphoreSlim(1, 1));
	private readonly Dictionary<Provider, IPromptRunner> _runners = runners.ToDictionary(runner => runner.Provider);

	public async Task Run(Provider provider, string cycleKey, CancellationToken cancellationToken)
	{
		var settings = await settingsService.Get(cancellationToken);
		var run = await runs.TryStartAutomatic(provider, cycleKey, settings.Triggers.For(provider).Model, time.GetUtcNow(), cancellationToken);
		if (run is null)
		{
			return;
		}

		logger.LogInformation("{Provider} automatic trigger for cycle {CycleKey}, attempt {Attempt}/{MaxAttempts}", provider, cycleKey, run.Attempts, MaxAttempts);
		var completed = await Execute(run, cancellationToken);

		if (completed.Status == TriggerStatus.Succeeded)
		{
			await notifications.Notify(NotificationKind.TriggerSucceeded, provider, "Prompt envoyé : un nouveau cycle est ouvert.", cancellationToken);
		}
		else if (completed.NextRetryAt is { } retryAt)
		{
			logger.LogWarning("{Provider} automatic trigger will be retried at {RetryAt}", provider, retryAt);
			scheduler.ScheduleTriggerRetry(provider, retryAt);
		}
		else
		{
			await notifications.Notify(NotificationKind.TriggerFailed, provider, $"{completed.ErrorCode} : {completed.Error}", cancellationToken);
		}
	}

	public async Task<TriggerRun> RequestManual(Provider provider, CancellationToken cancellationToken)
	{
		// The check and the insert form one step: two simultaneous requests start a single run.
		var gate = _requestGates[provider];
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (locks.IsBusy(provider) || await runs.GetRunning(provider, cancellationToken) is { })
			{
				throw new ProviderException(ProviderErrorCodes.CliBusy, "A CLI process is already running for this provider.");
			}

			var settings = await settingsService.Get(cancellationToken);
			var run = await runs.StartManual(provider, settings.Triggers.For(provider).Model, time.GetUtcNow(), cancellationToken);
			scheduler.EnqueueTrigger(run.Id);
			return run;
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task ExecuteManual(string runId, CancellationToken cancellationToken)
	{
		var run = await Get(runId, cancellationToken);
		if (run.Status != TriggerStatus.Running)
		{
			return;
		}

		using var providerLock = await locks.TryAcquire(run.Provider, ProviderLocks.JobWait, cancellationToken);
		if (providerLock is null)
		{
			logger.LogWarning("{Provider} manual trigger {RunId} given up: the provider is still busy after {Wait}", run.Provider, run.Id, ProviderLocks.JobWait);
			await runs.Complete(run.Id, TriggerStatus.Failed, time.GetUtcNow(), ProviderErrorCodes.CliBusy, "Another CLI process kept the provider busy.", null, CancellationToken.None);
			return;
		}

		// Manual triggers are never notified: the user is in front of the application.
		await Execute(run, cancellationToken);
	}

	public async Task<TriggerRun> Get(string runId, CancellationToken cancellationToken)
	{
		return await runs.Get(runId, cancellationToken) ?? throw new ResourceNotFoundException($"Trigger run {runId} does not exist.");
	}

	public async Task<int> RecoverInterrupted(CancellationToken cancellationToken)
	{
		var running = await runs.GetAllRunning(cancellationToken);
		var now = time.GetUtcNow();
		foreach (var run in running)
		{
			var retryAt = RetryAt(run, ProviderErrorCodes.Interrupted, now, TimeSpan.Zero);
			await runs.Complete(run.Id, TriggerStatus.Failed, now, ProviderErrorCodes.Interrupted, "Interrupted by a service restart.", retryAt, cancellationToken);
		}

		return running.Count;
	}

	/// <summary>
	///     Transient failures: an overloaded provider, a CLI that timed out or exited, a run cut by a restart or a cancellation.
	///     Never a login, usage limit or configuration error.
	/// </summary>
	public static bool IsTransient(string? errorCode)
	{
		return errorCode is ProviderErrorCodes.Overloaded or ProviderErrorCodes.Timeout or ProviderErrorCodes.CliExited or ProviderErrorCodes.Interrupted or ProviderErrorCodes.Cancelled;
	}

	private async Task<TriggerRun> Execute(TriggerRun run, CancellationToken cancellationToken)
	{
		try
		{
			await _runners[run.Provider].Run(run.Model, cancellationToken);
			return await runs.Complete(run.Id, TriggerStatus.Succeeded, time.GetUtcNow(), null, null, null, CancellationToken.None);
		}
		catch (ProviderException exception)
		{
			logger.LogWarning("{Provider} trigger failed: {Code} {Message}", run.Provider, exception.Code, exception.Message);
			return await Fail(run, exception.Code, exception.Message);
		}
		catch (OperationCanceledException)
		{
			// The job is stopping (service shutdown or deleted job): the run ends, it never stays running.
			logger.LogWarning("{Provider} trigger cancelled", run.Provider);
			await Fail(run, ProviderErrorCodes.Cancelled, "The trigger was cancelled before its end.", TimeSpan.Zero);
			throw;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "{Provider} trigger failed unexpectedly", run.Provider);
			return await Fail(run, ProviderErrorCodes.TriggerFailed, exception.Message);
		}
	}

	private Task<TriggerRun> Fail(TriggerRun run, string code, string message, TimeSpan? retryDelay = null)
	{
		var now = time.GetUtcNow();
		return runs.Complete(run.Id, TriggerStatus.Failed, now, code, message, RetryAt(run, code, now, retryDelay), CancellationToken.None);
	}

	/// <summary>
	///     An automatic run failed on a transient error is retried, at most <see cref="MaxAttempts" /> attempts in all.
	/// </summary>
	private static DateTimeOffset? RetryAt(TriggerRun run, string code, DateTimeOffset now, TimeSpan? delay)
	{
		if (run.Manual || !IsTransient(code) || run.Attempts >= MaxAttempts)
		{
			return null;
		}

		return now + (delay ?? RetryDelays[run.Attempts - 1]);
	}
}
