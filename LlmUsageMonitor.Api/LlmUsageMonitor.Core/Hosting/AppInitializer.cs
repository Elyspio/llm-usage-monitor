using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Hosting;

/// <summary>
///     On start: storage, default settings, runs interrupted by the previous process, poll jobs and a first reading.
/// </summary>
public sealed class AppInitializer(
	IStorageInitializer storage,
	ISettingsService settingsService,
	ITriggerRunRepository runs,
	IJobScheduler scheduler,
	TimeProvider time,
	ILogger<AppInitializer> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await storage.Initialize(cancellationToken);
		var settings = await settingsService.Get(cancellationToken);

		var interrupted = await runs.FailRunning(time.GetUtcNow(), ProviderErrorCodes.Interrupted, "Interrupted by a service restart.", cancellationToken);
		if (interrupted > 0) logger.LogWarning("{Count} trigger runs were interrupted by the previous process", interrupted);

		foreach (var provider in Enum.GetValues<Provider>())
		{
			scheduler.SetPollInterval(provider, settings.Polling.For(provider));
			scheduler.EnqueuePoll(provider);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
