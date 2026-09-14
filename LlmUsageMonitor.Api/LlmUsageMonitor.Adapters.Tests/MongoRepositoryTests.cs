using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Adapters.MongoDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shouldly;
using Testcontainers.MongoDb;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class MongoFixture : IAsyncLifetime
{
	private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();

	/// <summary>A fresh database per test, on the shared container.</summary>
	public async Task<ServiceProvider> CreateServices()
	{
		var connectionString = new MongoUrlBuilder(_container.GetConnectionString())
		{
			DatabaseName = $"tests-{Guid.NewGuid():N}",
			AuthenticationSource = "admin",
		}.ToString();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MongoDB"] = connectionString }).Build();

		var services = new ServiceCollection();
		new MongoAdapterModule().Load(services, configuration);
		var provider = services.BuildServiceProvider();
		await provider.GetRequiredService<IStorageInitializer>().Initialize(CancellationToken.None);
		return provider;
	}

	public async ValueTask InitializeAsync() => await _container.StartAsync();

	public ValueTask DisposeAsync() => _container.DisposeAsync();
}

public sealed class MongoRepositoryTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
	private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_unique_index_allows_one_automatic_trigger_per_provider_and_cycle()
	{
		await using var services = await mongo.CreateServices();
		var runs = services.GetRequiredService<ITriggerRunRepository>();

		var first = await runs.TryStartAutomatic(Provider.Codex, "resets:2026-09-14T11:59Z", "luna", Now, Token);
		var duplicate = await runs.TryStartAutomatic(Provider.Codex, "resets:2026-09-14T11:59Z", "luna", Now, Token);
		var otherProvider = await runs.TryStartAutomatic(Provider.Claude, "resets:2026-09-14T11:59Z", "haiku", Now, Token);
		await runs.StartManual(Provider.Codex, "luna", Now, Token);
		await runs.StartManual(Provider.Codex, "luna", Now, Token);

		first.ShouldNotBeNull();
		duplicate.ShouldBeNull();
		otherProvider.ShouldNotBeNull();
		(await runs.GetRecent(10, Token)).Count.ShouldBe(4);
	}

	[Fact]
	public async Task A_run_is_completed_and_interrupted_runs_are_failed_on_start()
	{
		await using var services = await mongo.CreateServices();
		var runs = services.GetRequiredService<ITriggerRunRepository>();
		var done = await runs.StartManual(Provider.Codex, "luna", Now, Token);
		await runs.StartManual(Provider.Claude, "haiku", Now, Token);

		var completed = await runs.Complete(done.Id, TriggerStatus.Failed, Now.AddSeconds(12), "USAGE_LIMIT", "limit", Token);
		var interrupted = await runs.FailRunning(Now.AddMinutes(1), "INTERRUPTED", "restart", Token);

		completed.DurationMs.ShouldBe(12_000);
		interrupted.ShouldBe(1);
		(await runs.GetRunning(Provider.Claude, Token)).ShouldBeNull();
	}

	[Fact]
	public async Task Snapshots_live_in_a_time_series_collection_kept_30_days()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();

		var collection = await (await database.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", "usageSnapshots") }, Token)).SingleAsync(Token);

		collection["type"].AsString.ShouldBe("timeseries");
		collection["options"]["timeseries"]["timeField"].AsString.ShouldBe("fetchedAt");
		collection["options"]["expireAfterSeconds"].ToInt64().ShouldBe((long)TimeSpan.FromDays(30).TotalSeconds);
	}

	[Fact]
	public async Task History_groups_the_snapshots_per_window_in_time_order()
	{
		await using var services = await mongo.CreateServices();
		var snapshots = services.GetRequiredService<IUsageSnapshotRepository>();
		await snapshots.Add(Provider.Claude, new UsageReading(Now.AddMinutes(-3), [new UsageWindow("five_hour", 10, Now.AddHours(2), 300), new UsageWindow("seven_day", 20, null, 10_080)]), Token);
		await snapshots.Add(Provider.Claude, new UsageReading(Now, [new UsageWindow("five_hour", 12, Now.AddHours(2), 300)]), Token);

		var series = await snapshots.GetHistory(Provider.Claude, null, Now.AddHours(-1), Now, Token);

		series.Select(item => item.WindowId).ShouldBe(["five_hour", "seven_day"]);
		series[0].Points.Select(point => point.UsedPercent).ShouldBe([10, 12]);
		series[0].Points[0].ResetsAt.ShouldBe(Now.AddHours(2));
	}

	[Fact]
	public async Task Provider_state_and_settings_round_trip()
	{
		await using var services = await mongo.CreateServices();
		var states = services.GetRequiredService<IProviderStateRepository>();
		var settingsRepository = services.GetRequiredService<ISettingsRepository>();
		var state = new ProviderState(Provider.Claude)
		{
			LastReading = new UsageReading(Now, [new UsageWindow("five_hour", 0, null, 300)]),
			LastFailure = new ProviderFailure("RATE_LIMITED", "HTTP 429", Now),
			BackoffLevel = 2,
			BackoffUntil = Now.AddMinutes(30),
			ActiveAlerts = [NotificationKind.AuthExpired],
			CurrentCycleKey = "reset:1",
			PendingResetCheck = new ScheduledJob("42", Now.AddHours(1)),
			TokenExpiresAt = Now.AddHours(8),
		};
		var settings = AppSettings.CreateDefault(false) with { Polling = new PollingSettings(5, 7) };

		await states.Save(state, Token);
		await settingsRepository.Save(settings, Token);
		var loaded = await states.Get(Provider.Claude, Token);

		loaded.LastReading!.Windows.ShouldHaveSingleItem().Id.ShouldBe("five_hour");
		(loaded with { LastReading = state.LastReading, ActiveAlerts = state.ActiveAlerts }).ShouldBe(state);
		loaded.ActiveAlerts.ShouldBe([NotificationKind.AuthExpired]);
		(await settingsRepository.Find(Token)).ShouldBe(settings);
		(await states.Get(Provider.Codex, Token)).ShouldBe(new ProviderState(Provider.Codex));
	}
}
