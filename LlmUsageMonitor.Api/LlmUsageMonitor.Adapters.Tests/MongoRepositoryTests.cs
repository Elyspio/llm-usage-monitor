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

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();
	}

	public ValueTask DisposeAsync()
	{
		return _container.DisposeAsync();
	}

	/// <summary>The URL of a fresh database on the shared container.</summary>
	public string CreateDatabaseUrl()
	{
		return new MongoUrlBuilder(_container.GetConnectionString())
		{
			DatabaseName = $"tests-{Guid.NewGuid():N}",
			AuthenticationSource = "admin"
		}.ToString();
	}

	/// <summary>The MongoDB adapter on a fresh database per test, or on <paramref name="connectionString" /> when given.</summary>
	public async Task<ServiceProvider> CreateServices(string? connectionString = null)
	{
		connectionString ??= CreateDatabaseUrl();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MongoDB"] = connectionString }).Build();

		var services = new ServiceCollection();
		services.AddLogging();
		new MongoAdapterModule().Load(services, configuration);
		var provider = services.BuildServiceProvider();
		await provider.GetRequiredService<IStorageInitializer>().Initialize(CancellationToken.None);
		return provider;
	}
}

public sealed class MongoRepositoryTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
	[Fact]
	public async Task Reset_credit_journal_roundtrips_and_guards_each_automatic_redemption()
	{
		await using var services = await mongo.CreateServices();
		var repository = services.GetRequiredService<IResetCreditRunRepository>();
		var run = new ResetCreditRun(Guid.NewGuid().ToString(), Provider.Codex, "credit", false, "codex:credit:1", Now, Now.AddHours(1), ResetCreditRunStatus.Running,
			Before: [new("codex/primary", 40, Now.AddDays(1), 10080)]);
		await repository.Start(run, Token);
		var duplicate = await repository.Start(run with { Id = Guid.NewGuid().ToString() }, Token);
		duplicate.Id.ShouldBe(run.Id);
		(await repository.GetAutomatic(run.AutomaticKey!, Token))!.Id.ShouldBe(run.Id);
		(await repository.GetPending(Provider.Codex, Token)).ShouldHaveSingleItem().Before!.ShouldHaveSingleItem().UsedPercent.ShouldBe(40);
		await repository.Save(run with { Status = ResetCreditRunStatus.Succeeded, EndedAt = Now, Outcome = "reset" }, Token);
		(await repository.GetPending(Provider.Codex, Token)).ShouldBeEmpty();
		(await repository.GetRecent(Provider.Codex, 10, Token)).ShouldHaveSingleItem().Status.ShouldBe(ResetCreditRunStatus.Succeeded);
		(await ExpireAfter(services.GetRequiredService<IMongoDatabase>(), "resetCreditRuns", "endedAt")).ShouldBe((long)TimeSpan.FromDays(90).TotalSeconds);
	}

	[Fact]
	public async Task Reset_credit_state_roundtrips_and_settings_saves_preserve_other_sections()
	{
		await using var services = await mongo.CreateServices();
		var states = services.GetRequiredService<IProviderStateRepository>();
		var state = new ProviderState(Provider.Claude)
		{
			ResetCredits = new(3, [new("grant", 3, Now.AddDays(1), Now, "Reset", true, false, ["seven_day"])]),
			CreditResetSuppressesTrigger = true,
			CreditResetRefreshPending = true,
			CreditResetWindowResetsAt = Now,
			PendingCreditCheck = new("job", Now.AddMinutes(10))
		};
		await states.Save(state, Token);
		var fetched = await states.Get(Provider.Claude, Token);
		fetched.ResetCredits!.Credits!.ShouldHaveSingleItem().ExpiresAt.ShouldBe(Now.AddDays(1));
		fetched.CreditResetSuppressesTrigger.ShouldBeTrue();
		fetched.PendingCreditCheck.ShouldBe(state.PendingCreditCheck);
		var settings = services.GetRequiredService<ISettingsRepository>();
		await settings.Initialize(AppSettings.CreateDefault(false), Token);
		await settings.SaveResetCredits(new(new(true, 15), new(false, 60)), Token);
		await settings.SavePolling(new(5, 10), Token);
		var saved = (await settings.Find(Token))!;
		saved.ResetCredits.Claude.ShouldBe(new ProviderResetCreditSettings(true, 15));
		saved.Polling.ShouldBe(new PollingSettings(5, 10));
		// An existing production document without the new section must remain opt-in.
		await services.GetRequiredService<IMongoDatabase>().GetCollection<BsonDocument>("settings").UpdateOneAsync(
			new BsonDocument("_id", "global"), new BsonDocument("$unset", new BsonDocument { ["claudeResetCredits"] = "", ["codexResetCredits"] = "" }), cancellationToken: Token);
		(await settings.Find(Token))!.ResetCredits.ShouldBe(ResetCreditSettings.Default);
	}
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
	public async Task A_run_is_completed_and_the_runs_left_running_are_listed()
	{
		await using var services = await mongo.CreateServices();
		var runs = services.GetRequiredService<ITriggerRunRepository>();
		var done = await runs.StartManual(Provider.Codex, "luna", Now, Token);
		var running = await runs.StartManual(Provider.Claude, "haiku", Now, Token);

		var completed = await runs.Complete(done.Id, TriggerStatus.Failed, Now.AddSeconds(12), "USAGE_LIMIT", "limit", null, Token);

		completed.DurationMs.ShouldBe(12_000);
		completed.Attempts.ShouldBe(1);
		(await runs.GetAllRunning(Token)).ShouldHaveSingleItem().Id.ShouldBe(running.Id);
	}

	[Fact]
	public async Task A_failed_automatic_run_starts_again_once_its_retry_is_due()
	{
		await using var services = await mongo.CreateServices();
		var runs = services.GetRequiredService<ITriggerRunRepository>();
		const string cycle = "resets:2026-09-14T11:59Z";
		var first = await runs.TryStartAutomatic(Provider.Codex, cycle, "luna", Now, Token);
		await runs.Complete(first!.Id, TriggerStatus.Failed, Now.AddSeconds(5), "OVERLOADED", "529", Now.AddMinutes(2), Token);

		var early = await runs.TryStartAutomatic(Provider.Codex, cycle, "luna", Now.AddMinutes(1), Token);
		var retried = await runs.TryStartAutomatic(Provider.Codex, cycle, "luna-2", Now.AddMinutes(2), Token);
		var concurrent = await runs.TryStartAutomatic(Provider.Codex, cycle, "luna", Now.AddMinutes(2), Token);

		early.ShouldBeNull();
		concurrent.ShouldBeNull();
		retried.ShouldNotBeNull();
		retried.Id.ShouldBe(first.Id);
		(retried.Status, retried.Attempts, retried.Model, retried.StartedAt).ShouldBe((TriggerStatus.Running, 2, "luna-2", Now.AddMinutes(2)));
		(retried.EndedAt, retried.ErrorCode, retried.NextRetryAt).ShouldBe((null, null, null));
	}

	[Fact]
	public async Task A_run_stored_before_retries_existed_counts_as_one_attempt()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();
		var id = ObjectId.GenerateNewId();
		await database.GetCollection<BsonDocument>("triggerRuns").InsertOneAsync(new BsonDocument
		{
			["_id"] = id,
			["provider"] = "Codex",
			["manual"] = false,
			["cycleKey"] = "reset:1",
			["model"] = "luna",
			["status"] = "Failed",
			["startedAt"] = Now.UtcDateTime,
			["endedAt"] = Now.UtcDateTime,
			["errorCode"] = "TIMEOUT",
			["error"] = "slow",
			["nextRetryAt"] = Now.UtcDateTime
		}, cancellationToken: Token);
		var runs = services.GetRequiredService<ITriggerRunRepository>();

		(await runs.Get(id.ToString(), Token))!.Attempts.ShouldBe(1);
		(await runs.TryStartAutomatic(Provider.Codex, "reset:1", "luna", Now, Token))!.Attempts.ShouldBe(2);
	}

	[Fact]
	public async Task A_reset_is_stored_once_per_transition()
	{
		await using var services = await mongo.CreateServices();
		var resets = services.GetRequiredService<IResetRepository>();

		var first = await resets.TryAdd(Provider.Codex, "codex/primary", Now.AddMinutes(-3), Now, 40, 5, Now.AddMinutes(-1), Token);
		var replayed = await resets.TryAdd(Provider.Codex, "codex/primary", Now.AddMinutes(-3), Now.AddMinutes(3), 40, 5, Now.AddMinutes(-1), Token);
		var otherWindow = await resets.TryAdd(Provider.Codex, "codex/secondary", Now.AddMinutes(-3), Now, 40, 5, null, Token);
		var next = await resets.TryAdd(Provider.Codex, "codex/primary", Now.AddHours(5), Now.AddHours(5).AddMinutes(3), 30, 0, Now.AddHours(5), Token);

		first.ShouldNotBeNull();
		replayed.ShouldBeNull();
		otherWindow.ShouldNotBeNull();
		next.ShouldNotBeNull();
		(await resets.GetLast(Provider.Codex, "codex/primary", Token))!.Id.ShouldBe(next.Id);
	}

	[Fact]
	public async Task Snapshots_live_in_a_time_series_collection_kept_30_days()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();

		var collection = await (await database.ListCollectionsAsync(new() { Filter = new BsonDocument("name", "usageSnapshots") }, Token)).SingleAsync(Token);

		collection["type"].AsString.ShouldBe("timeseries");
		collection["options"]["timeseries"]["timeField"].AsString.ShouldBe("fetchedAt");
		collection["options"]["expireAfterSeconds"].ToInt64().ShouldBe((long)TimeSpan.FromDays(30).TotalSeconds);
	}

	[Fact]
	public async Task History_groups_the_snapshots_per_window_in_time_order()
	{
		await using var services = await mongo.CreateServices();
		var snapshots = services.GetRequiredService<IUsageSnapshotRepository>();
		await snapshots.Add(Provider.Claude, new(Now.AddMinutes(-3), [new("five_hour", 10, Now.AddHours(2), 300), new("seven_day", 20, null, 10_080)]), Token);
		await snapshots.Add(Provider.Claude, new(Now, [new("five_hour", 12, Now.AddHours(2), 300)]), Token);

		var series = await snapshots.GetHistory(Provider.Claude, null, Now.AddHours(-1), Now, TimeSpan.FromMinutes(1), Token);

		series.Select(item => item.WindowId).ShouldBe(["five_hour", "seven_day"]);
		series[0].Points.Select(point => point.UsedPercent).ShouldBe([10, 12]);
		series[0].Points[0].ResetsAt.ShouldBe(Now.AddHours(2));
	}

	[Fact]
	public async Task History_keeps_the_last_reading_of_each_bucket()
	{
		await using var services = await mongo.CreateServices();
		var snapshots = services.GetRequiredService<IUsageSnapshotRepository>();
		var start = new DateTimeOffset(2026, 9, 14, 11, 0, 0, TimeSpan.Zero);
		for (var minute = 0; minute < 60; minute += 3)
			await snapshots.Add(Provider.Codex, new(start.AddMinutes(minute), [new("codex/primary", minute, start.AddHours(5), 300)]), Token);

		var fiveMinutes = (await snapshots.GetHistory(Provider.Codex, "codex/primary", start, Now, TimeSpan.FromMinutes(5), Token)).ShouldHaveSingleItem();
		var hourly = (await snapshots.GetHistory(null, null, start, Now, TimeSpan.FromHours(1), Token)).ShouldHaveSingleItem();

		fiveMinutes.Points.Count.ShouldBe(12);
		fiveMinutes.Points.Select(point => point.UsedPercent).Take(3).ShouldBe([3, 9, 12]);
		fiveMinutes.Points[0].FetchedAt.ShouldBe(start.AddMinutes(3));
		fiveMinutes.Points[0].ResetsAt.ShouldBe(start.AddHours(5));
		var point = hourly.Points.ShouldHaveSingleItem();
		(point.UsedPercent, point.FetchedAt).ShouldBe((57, start.AddMinutes(57)));
	}

	[Fact]
	public async Task Resets_and_trigger_runs_expire_after_90_days()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();

		(await ExpireAfter(database, "resets", "detectedAt")).ShouldBe((long)TimeSpan.FromDays(90).TotalSeconds);
		(await ExpireAfter(database, "triggerRuns", "startedAt")).ShouldBe((long)TimeSpan.FromDays(90).TotalSeconds);
	}

	[Fact]
	public async Task The_retention_is_applied_to_collections_created_before_it()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();
		// As created by an earlier version: snapshots kept 7 days, a plain sort index on the runs.
		await database.DropCollectionAsync("usageSnapshots", Token);
		await database.CreateCollectionAsync("usageSnapshots", new() { TimeSeriesOptions = new("fetchedAt", "meta", TimeSeriesGranularity.Minutes), ExpireAfter = TimeSpan.FromDays(7) },
			Token);
		await database.DropCollectionAsync("triggerRuns", Token);
		await database.GetCollection<BsonDocument>("triggerRuns").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument("startedAt", -1)), cancellationToken: Token);

		await services.GetRequiredService<IStorageInitializer>().Initialize(Token);

		var collection = await (await database.ListCollectionsAsync(new() { Filter = new BsonDocument("name", "usageSnapshots") }, Token)).SingleAsync(Token);
		collection["options"]["expireAfterSeconds"].ToInt64().ShouldBe((long)TimeSpan.FromDays(30).TotalSeconds);
		(await ExpireAfter(database, "triggerRuns", "startedAt")).ShouldBe((long)TimeSpan.FromDays(90).TotalSeconds);
	}

	private static async Task<long?> ExpireAfter(IMongoDatabase database, string collection, string field)
	{
		var indexes = await (await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync(Token)).ToListAsync(Token);
		var index = indexes.Single(candidate => candidate["key"].AsBsonDocument.Names.SequenceEqual([field]));
		return index.TryGetValue("expireAfterSeconds", out var seconds) ? seconds.ToInt64() : null;
	}

	[Fact]
	public async Task Provider_state_and_settings_round_trip()
	{
		await using var services = await mongo.CreateServices();
		var states = services.GetRequiredService<IProviderStateRepository>();
		var settingsRepository = services.GetRequiredService<ISettingsRepository>();
		var state = new ProviderState(Provider.Claude)
		{
			LastReading = new(Now, [new("five_hour", 0, null, 300)]),
			LastFailure = new("RATE_LIMITED", "HTTP 429", Now),
			BackoffLevel = 2,
			BackoffUntil = Now.AddMinutes(30),
			ActiveAlerts = [NotificationKind.AuthExpired],
			CurrentCycleKey = "reset:1",
			PendingResetCheck = new("42", Now.AddHours(1)),
			TokenExpiresAt = Now.AddHours(8)
		};
		var defaults = AppSettings.CreateDefault(false);
		var settings = defaults with
		{
			Polling = new(5, 7),
			Notifications = defaults.Notifications with
			{
				Events = new(NotificationEvents.Default, NotificationEvents.Default with { Reset = true })
			}
		};

		await states.Save(state, Token);
		await settingsRepository.Initialize(settings, Token);
		await settingsRepository.Initialize(defaults, Token);
		var loaded = await states.Get(Provider.Claude, Token);

		loaded.LastReading!.Windows.ShouldHaveSingleItem().Id.ShouldBe("five_hour");
		(loaded with { LastReading = state.LastReading, ActiveAlerts = state.ActiveAlerts }).ShouldBe(state);
		loaded.ActiveAlerts.ShouldBe([NotificationKind.AuthExpired]);
		var savedSettings = await settingsRepository.Find(Token);
		savedSettings.ShouldBe(settings);
		savedSettings!.Notifications.Events.Claude.Reset.ShouldBeFalse();
		savedSettings.Notifications.Events.Codex.Reset.ShouldBeTrue();
		(await states.Get(Provider.Codex, Token)).ShouldBe(new(Provider.Codex));
	}

	[Fact]
	public async Task Each_settings_section_is_written_alone()
	{
		await using var services = await mongo.CreateServices();
		var repository = services.GetRequiredService<ISettingsRepository>();
		var defaults = AppSettings.CreateDefault(true);
		await repository.Initialize(defaults, Token);
		var failure = new NotificationSendFailure(Now, "ntfy returned HTTP 502.");

		// Saves of different sections, all at once, as a user save racing with background delivery failures.
		var writes = Enumerable.Range(0, 20).SelectMany(i => new[]
		{
			repository.SaveSendFailure(failure with { At = Now.AddSeconds(i) }, Token),
			repository.SavePolling(new(5, 10), Token),
			repository.SaveTriggers(new(new(false, "sonnet"), new(true, "luna")), Token),
			repository.SaveNotifications(defaults.Notifications with { Topic = "topic_1", ReadFailureThreshold = 7 }, Token)
		});
		await Task.WhenAll(writes);

		var saved = (await repository.Find(Token))!;
		saved.Polling.ShouldBe(new(5, 10));
		saved.Triggers.Claude.ShouldBe(new(false, "sonnet"));
		(saved.Notifications.Topic, saved.Notifications.ReadFailureThreshold).ShouldBe(("topic_1", 7));
		saved.Notifications.LastSendFailure!.Message.ShouldBe(failure.Message);

		await repository.SaveSendFailure(null, Token);
		(await repository.Find(Token))!.Notifications.LastSendFailure.ShouldBeNull();
	}

	[Fact]
	public async Task Legacy_notification_events_are_applied_to_both_providers()
	{
		await using var services = await mongo.CreateServices();
		var database = services.GetRequiredService<IMongoDatabase>();
		var settings = database.GetCollection<BsonDocument>("settings");
		await settings.InsertOneAsync(
			new BsonDocument
			{
				["_id"] = "global",
				["claudeIntervalMinutes"] = 3,
				["codexIntervalMinutes"] = 3,
				["claudeTrigger"] = new BsonDocument { ["autoEnabled"] = false, ["model"] = "haiku" },
				["codexTrigger"] = new BsonDocument { ["autoEnabled"] = false, ["model"] = "gpt-5.6-luna" },
				["notifications"] = new BsonDocument
				{
					["url"] = "https://ntfy.sh",
					["events"] = new BsonDocument
					{
						["triggerFailed"] = false,
						["authExpired"] = true,
						["readFailed"] = true,
						["reset"] = false,
						["triggerSucceeded"] = true,
						["recovered"] = true
					},
					["readFailureThreshold"] = 3
				}
			},
			cancellationToken: Token);

		var loaded = await services.GetRequiredService<ISettingsRepository>().Find(Token);

		loaded.ShouldNotBeNull();
		loaded.Notifications.Events.Claude.ShouldBe(loaded.Notifications.Events.Codex);
		loaded.Notifications.Events.Claude.TriggerFailed.ShouldBeFalse();
		loaded.Notifications.Events.Claude.ResetCreditSucceeded.ShouldBeTrue();
		loaded.Notifications.Events.Claude.ResetCreditFailed.ShouldBeTrue();

		await services.GetRequiredService<ISettingsRepository>().SaveNotifications(loaded.Notifications, Token);
		var raw = await settings.Find(new BsonDocument("_id", "global")).SingleAsync(Token);
		raw["notifications"].AsBsonDocument.Contains("events").ShouldBeFalse();
		raw["notifications"]["providerEvents"]["codex"]["triggerFailed"].AsBoolean.ShouldBeFalse();
	}
}
