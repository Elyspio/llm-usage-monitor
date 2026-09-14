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
///     Hangfire runs inside the API process, with its own database on the application MongoDB server.
/// </summary>
public sealed class HangfireAdapterModule : IModule
{
	public const string DatabaseName = "hangfire";

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		// Integration tests and the build-time OpenAPI generation never start the Hangfire server: jobs are only logged.
		if (!IsEnabled(configuration))
		{
			services.AddSingleton<IJobScheduler, LoggingJobScheduler>();
			return;
		}

		var connectionString = configuration.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is required.");
		var hangfireUrl = new MongoUrlBuilder(connectionString) { DatabaseName = DatabaseName }.ToString();

		services.AddHangfire(config => config
			.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
			.UseSimpleAssemblyNameTypeSerializer()
			.UseRecommendedSerializerSettings()
			// Each job decides on its own retries: a failed trigger is never replayed.
			.UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
			.UseMongoStorage(hangfireUrl, new MongoStorageOptions
			{
				MigrationOptions = new MongoMigrationOptions
				{
					MigrationStrategy = new MigrateMongoMigrationStrategy(),
					BackupStrategy = new NoneMongoBackupStrategy(),
				},
				// The Aspire MongoDB container is a standalone server, without the replica set change streams need.
				CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection,
				Prefix = "hangfire",
			}));
		services.AddHangfireServer(options => options.WorkerCount = 4);

		services.AddTransient<ProviderJobs>();
		services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
	}

	public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue("Hangfire:Enabled", true) && !OpenApiGeneration.IsRunning;
}

/// <summary>
///     Stand-in scheduler when Hangfire is disabled: nothing runs, every request is logged.
/// </summary>
internal sealed class LoggingJobScheduler(ILogger<LoggingJobScheduler> logger) : IJobScheduler
{
	public void SetPollInterval(Provider provider, int minutes) => logger.LogInformation("Hangfire disabled: poll of {Provider} every {Minutes} min not scheduled", provider, minutes);

	public void EnqueuePoll(Provider provider) => logger.LogInformation("Hangfire disabled: poll of {Provider} not queued", provider);

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

	public void EnqueueTrigger(string runId) => logger.LogInformation("Hangfire disabled: trigger {RunId} not queued", runId);

	public void Delete(string jobId)
	{
		// Nothing was scheduled.
	}
}
