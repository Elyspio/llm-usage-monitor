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
			return;
		}

		var connectionString = configuration.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is required.");
		// The connection string of the Aspire resource carries no database name, which Hangfire.Mongo requires.
		var storageUrl = new MongoUrlBuilder(connectionString) { DatabaseName = MongoUrl.Create(connectionString).DatabaseName ?? StorageDefaults.DatabaseName }.ToString();

		services.AddHangfire(config => config
			.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
			.UseSimpleAssemblyNameTypeSerializer()
			.UseRecommendedSerializerSettings()
			// Each job decides on its own retries: a failed trigger is never replayed.
			.UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
			.UseMongoStorage(storageUrl, new MongoStorageOptions
			{
				MigrationOptions = new()
				{
					MigrationStrategy = new MigrateMongoMigrationStrategy(),
					BackupStrategy = new NoneMongoBackupStrategy()
				},
				// The Aspire MongoDB container is a standalone server, without the replica set change streams need.
				CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection,
				Prefix = CollectionPrefix
			}));
		services.AddHangfireServer(options => options.WorkerCount = 4);

		services.AddTransient<ProviderJobs>();
		services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
	}

	public static bool IsEnabled(IConfiguration configuration)
	{
		return configuration.GetValue("Hangfire:Enabled", true) && !OpenApiGeneration.IsRunning;
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

	public string ScheduleKeepAlive(DateTimeOffset runAt)
	{
		logger.LogInformation("Hangfire disabled: Claude keep-alive at {RunAt} not scheduled", runAt);
		return "disabled";
	}

	public void EnqueueTrigger(string runId)
	{
		logger.LogInformation("Hangfire disabled: trigger {RunId} not queued", runId);
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