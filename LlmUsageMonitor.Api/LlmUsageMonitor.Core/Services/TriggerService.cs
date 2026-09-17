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
	/// <summary>Runs the prompt once for the cycle; does nothing when the cycle already had its trigger.</summary>
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
	private readonly Dictionary<Provider, IPromptRunner> _runners = runners.ToDictionary(runner => runner.Provider);

	public async Task Run(Provider provider, string cycleKey, CancellationToken cancellationToken)
	{
		var settings = await settingsService.Get(cancellationToken);
		var run = await runs.TryStartAutomatic(provider, cycleKey, settings.Triggers.For(provider).Model, time.GetUtcNow(), cancellationToken);
		if (run is null)
		{
			return;
		}

		logger.LogInformation("{Provider} automatic trigger for cycle {CycleKey}", provider, cycleKey);
		var completed = await Execute(run, cancellationToken);

		if (completed.Status == TriggerStatus.Succeeded)
		{
			await notifications.Notify(NotificationKind.TriggerSucceeded, provider, "Prompt envoyé : un nouveau cycle est ouvert.", cancellationToken);
		}
		else
			// No automatic retry: the cycle guard keeps this failure as the attempt of the cycle.
		{
			await notifications.Notify(NotificationKind.TriggerFailed, provider, $"{completed.ErrorCode} : {completed.Error}", cancellationToken);
		}
	}

	public async Task<TriggerRun> RequestManual(Provider provider, CancellationToken cancellationToken)
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

	public async Task ExecuteManual(string runId, CancellationToken cancellationToken)
	{
		var run = await Get(runId, cancellationToken);
		if (run.Status != TriggerStatus.Running)
		{
			return;
		}

		using var providerLock = await locks.Acquire(run.Provider, cancellationToken);
		// Manual triggers are never notified: the user is in front of the application.
		await Execute(run, cancellationToken);
	}

	public async Task<TriggerRun> Get(string runId, CancellationToken cancellationToken)
	{
		return await runs.Get(runId, cancellationToken) ?? throw new ResourceNotFoundException($"Trigger run {runId} does not exist.");
	}

	private async Task<TriggerRun> Execute(TriggerRun run, CancellationToken cancellationToken)
	{
		try
		{
			await _runners[run.Provider].Run(run.Model, cancellationToken);
			return await runs.Complete(run.Id, TriggerStatus.Succeeded, time.GetUtcNow(), null, null, CancellationToken.None);
		}
		catch (ProviderException exception)
		{
			logger.LogWarning("{Provider} trigger failed: {Code} {Message}", run.Provider, exception.Code, exception.Message);
			return await runs.Complete(run.Id, TriggerStatus.Failed, time.GetUtcNow(), exception.Code, exception.Message, CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "{Provider} trigger failed unexpectedly", run.Provider);
			return await runs.Complete(run.Id, TriggerStatus.Failed, time.GetUtcNow(), ProviderErrorCodes.TriggerFailed, exception.Message, CancellationToken.None);
		}
	}
}