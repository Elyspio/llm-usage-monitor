using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.Hangfire;

/// <summary>
///     Hangfire runs inside the API process, in the application database: its collections are prefixed, so a single Mongo
///     user with <c>readWrite</c> on that database covers the whole application.
/// </summary>
public sealed class HangfireAdapterModule : IModule
{
	public const string CollectionPrefix = "hangfire";

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		// Integration tests and the build-time OpenAPI generation never start the Hangfire server: jobs are only logged.
		if (!IsEnabled(configuration))
		{
			services.AddSingleton<IJobScheduler, LoggingJobScheduler>();
			services.AddSingleton<IJobServerMonitor, DisabledJobServerMonitor>();
			return;
		}

		// The storage shares the client and the database of the MongoDB adapter (MongoAdapterModule): one connection pool.
		services.AddHangfire((sp, config) => config
			.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
			.UseSimpleAssemblyNameTypeSerializer()
			.UseRecommendedSerializerSettings()
			// Hangfire never replays a job: the automatic trigger schedules its own bounded retries (TriggerService.RetryDelays).
			.UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
			.UseMongoStorage(sp.GetRequiredService<IMongoClient>(), sp.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName, new MongoStorageOptions
			{
				MigrationOptions = new()
				{
					MigrationStrategy = new MigrateMongoMigrationStrategy(),
					// A schema migration of a new Hangfire.Mongo version first copies the hangfire.* collections (suffix
					// "migrationbackup") in the same database: a failed migration can be rolled back.
					BackupStrategy = new CollectionMongoBackupStrategy()
				},
				// The Aspire MongoDB container is a standalone server, without the replica set change streams need.
				CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection,
				Prefix = CollectionPrefix
			}));
		services.AddHangfireServer(options => options.WorkerCount = 4);

		services.AddTransient<ProviderJobs>();
		services.AddTransient<FailedJobPurge>();
		services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
		services.AddSingleton<IJobServerMonitor, HangfireServerMonitor>();
	}

	public static bool IsEnabled(IConfiguration configuration)
	{
		return configuration.GetValue("Hangfire:Enabled", true) && !OpenApiGeneration.IsRunning;
	}
}

/// <summary>
///     The heartbeats the Hangfire servers write in the storage (every 30 s by default).
/// </summary>
internal sealed class HangfireServerMonitor(JobStorage storage) : IJobServerMonitor
{
	public Task<DateTimeOffset?> GetLastHeartbeat(CancellationToken cancellationToken)
	{
		// The monitoring API is synchronous.
		return Task.Run(() =>
		{
			var heartbeats = storage.GetMonitoringApi().Servers().Where(server => server.Heartbeat is { }).Select(server => server.Heartbeat!.Value).ToList();
			return heartbeats.Count == 0 ? (DateTimeOffset?)null : new DateTimeOffset(DateTime.SpecifyKind(heartbeats.Max(), DateTimeKind.Utc));
		}, cancellationToken);
	}
}

/// <summary>
///     Without Hangfire, no job is expected to run: the job server always looks alive.
/// </summary>
internal sealed class DisabledJobServerMonitor(TimeProvider time) : IJobServerMonitor
{
	public Task<DateTimeOffset?> GetLastHeartbeat(CancellationToken cancellationToken)
	{
		return Task.FromResult<DateTimeOffset?>(time.GetUtcNow());
	}
}

/// <summary>
///     Stand-in scheduler when Hangfire is disabled: nothing runs, every request is logged.
/// </summary>
internal sealed class LoggingJobScheduler(ILogger<LoggingJobScheduler> logger) : IJobScheduler
{
	public void SetPollInterval(Provider provider, int minutes)
	{
		logger.LogInformation("Hangfire disabled: poll of {Provider} every {Minutes} min not scheduled", provider, minutes);
	}

	public void EnqueuePoll(Provider provider)
	{
		logger.LogInformation("Hangfire disabled: poll of {Provider} not queued", provider);
	}

	public string SchedulePostResetCheck(Provider provider, DateTimeOffset runAt)
	{
		logger.LogInformation("Hangfire disabled: post-reset check of {Provider} at {RunAt} not scheduled", provider, runAt);
		return "disabled";
	}

	public string ScheduleTriggerRetry(Provider provider, DateTimeOffset runAt)
	{
		logger.LogInformation("Hangfire disabled: trigger retry of {Provider} at {RunAt} not scheduled", provider, runAt);
		return "disabled";
	}

	public string ScheduleKeepAlive(DateTimeOffset runAt)
	{
		logger.LogInformation("Hangfire disabled: Claude keep-alive at {RunAt} not scheduled", runAt);
		return "disabled";
	}

	public void EnqueueTrigger(string runId)
	{
		logger.LogInformation("Hangfire disabled: trigger {RunId} not queued", runId);
	}

	public void ScheduleJobPurge()
	{
		logger.LogInformation("Hangfire disabled: daily job purge not scheduled");
	}

	public void SchedulePriceRefresh()
	{
		logger.LogInformation("Hangfire disabled: daily model price refresh not scheduled");
	}

	public void EnqueuePriceRefresh()
	{
		logger.LogInformation("Hangfire disabled: model price refresh not queued");
	}

	public void Delete(string jobId)
	{
		// Nothing was scheduled.
	}
}