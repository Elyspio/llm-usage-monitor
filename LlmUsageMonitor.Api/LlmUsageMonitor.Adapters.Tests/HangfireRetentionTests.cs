using Hangfire;
using Hangfire.Common;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Hangfire.States;
using LlmUsageMonitor.Adapters.Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class HangfireRetentionTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Failed_jobs_are_purged_after_seven_days()
	{
		var url = MongoUrl.Create(mongo.CreateDatabaseUrl());
		var storage = new MongoStorage(MongoClientSettings.FromUrl(url), url.DatabaseName, new MongoStorageOptions
		{
			MigrationOptions = new() { MigrationStrategy = new MigrateMongoMigrationStrategy(), BackupStrategy = new NoneMongoBackupStrategy() },
			CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection,
			Prefix = HangfireAdapterModule.CollectionPrefix
		});
		// No global filter: the default automatic retry would turn the failure into a new schedule (the app sets 0 attempts).
		var client = new BackgroundJobClient(storage, new JobFilterCollection());
		var failed = client.Create(() => Console.WriteLine("failed"), new ScheduledState(TimeSpan.FromDays(1)));
		var scheduled = client.Create(() => Console.WriteLine("scheduled"), new ScheduledState(TimeSpan.FromDays(1)));
		client.ChangeState(failed, new FailedState(new InvalidOperationException("boom")));

		await new FailedJobPurge(storage, client, new ShiftedTime(TimeSpan.FromDays(6)), NullLogger<FailedJobPurge>.Instance).Purge(Token);
		State(storage, failed).ShouldBe(FailedState.StateName);

		await new FailedJobPurge(storage, client, new ShiftedTime(TimeSpan.FromDays(8)), NullLogger<FailedJobPurge>.Instance).Purge(Token);
		State(storage, failed).ShouldBe(DeletedState.StateName);
		State(storage, scheduled).ShouldBe(ScheduledState.StateName);
	}

	private static string State(JobStorage storage, string id)
	{
		using var connection = storage.GetConnection();
		return connection.GetJobData(id).State;
	}

	private sealed class ShiftedTime(TimeSpan shift) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow()
		{
			return base.GetUtcNow() + shift;
		}
	}
}
