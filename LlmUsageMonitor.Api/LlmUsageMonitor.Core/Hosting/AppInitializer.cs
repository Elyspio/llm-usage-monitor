using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Hosting;

/// <summary>
///     On start: storage, default settings, runs interrupted by the previous process, poll jobs, a first reading and the model prices.
/// </summary>
public sealed class AppInitializer(
	IStorageInitializer storage,
	ISettingsService settingsService,
	ITriggerService triggers,
	IModelPriceRepository prices,
	IJobScheduler scheduler,
	ILogger<AppInitializer> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await storage.Initialize(cancellationToken);
		var settings = await settingsService.Get(cancellationToken);

		// An interrupted automatic run is retried by the first reading below, if its cycle still waits.
		var interrupted = await triggers.RecoverInterrupted(cancellationToken);
		if (interrupted > 0)
		{
			logger.LogWarning("{Count} trigger runs were interrupted by the previous process", interrupted);
		}

		foreach (var provider in Enum.GetValues<Provider>())
		{
			scheduler.SetPollInterval(provider, settings.Polling.For(provider));
			scheduler.EnqueuePoll(provider);
		}

		scheduler.SchedulePriceRefresh();
		scheduler.ScheduleJobPurge();
		if (!await prices.Any(cancellationToken))
		{
			scheduler.EnqueuePriceRefresh();
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}