using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

/// <summary>
///     The start of the application, the scheduling of the Claude keep-alive and the cancellation of a reading.
/// </summary>
public sealed class LifecycleTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_start_prepares_the_storage_recovers_the_runs_and_schedules_every_job()
	{
		var harness = new TestHarness();
		harness.Runs.Start(Provider.Codex, false, "cycle", "gpt", Start.AddMinutes(-5));
		var storage = new RecordingStorage();
		var prices = new InMemoryPrices();

		await new AppInitializer(storage, harness.Settings, harness.Triggers, prices, harness.Scheduler, NullLogger<AppInitializer>.Instance).StartAsync(Token);

		storage.Initialized.ShouldBeTrue();
		var interrupted = harness.Runs.All.ShouldHaveSingleItem();
		interrupted.Status.ShouldBe(TriggerStatus.Failed);
		interrupted.ErrorCode.ShouldBe(ProviderErrorCodes.Interrupted);
		harness.Scheduler.PollIntervals.ShouldBe(new Dictionary<Provider, int> { [Provider.Claude] = 3, [Provider.Codex] = 3 });
		harness.Scheduler.EnqueuedPolls.ShouldBe([Provider.Claude, Provider.Codex]);
		harness.Scheduler.PriceRefreshScheduled.ShouldBeTrue();
		harness.Scheduler.JobPurgeScheduled.ShouldBeTrue();
		// No price yet: they are downloaded at once instead of waiting for the daily refresh.
		harness.Scheduler.EnqueuedPriceRefreshes.ShouldBe(1);
	}

	[Fact]
	public async Task The_known_prices_wait_for_the_daily_refresh()
	{
		var harness = new TestHarness();
		var prices = new InMemoryPrices();
		await prices.Save([new("claude-sonnet-4", 3, 15, null, null)], Token);

		await new AppInitializer(new RecordingStorage(), harness.Settings, harness.Triggers, prices, harness.Scheduler, NullLogger<AppInitializer>.Instance).StartAsync(Token);

		harness.Scheduler.EnqueuedPriceRefreshes.ShouldBe(0);
		harness.Scheduler.PriceRefreshScheduled.ShouldBeTrue();
	}

	[Fact]
	public async Task The_keep_alive_is_scheduled_four_minutes_before_the_expiry_and_moved_with_it()
	{
		var harness = new TestHarness();
		harness.Session.ExpiresAt = Start.AddHours(8);

		await harness.KeepAlive.Run(Token);
		harness.Scheduler.KeepAlives.ShouldHaveSingleItem().RunAt.ShouldBe(Start.AddHours(8).AddMinutes(-4));
		harness.States.Stored[Provider.Claude].TokenExpiresAt.ShouldBe(Start.AddHours(8));

		// Unchanged expiry: the scheduled job stays.
		await harness.KeepAlive.Run(Token);
		harness.Scheduler.KeepAlives.Count.ShouldBe(1);
		harness.Scheduler.Deleted.ShouldBeEmpty();

		// Another CLI process refreshed the token: the job follows the new expiry, the old one is deleted.
		harness.Session.ExpiresAt = Start.AddHours(10);
		await harness.KeepAlive.Run(Token);
		harness.Scheduler.KeepAlives.Count.ShouldBe(2);
		harness.Scheduler.KeepAlives[^1].RunAt.ShouldBe(Start.AddHours(10).AddMinutes(-4));
		harness.Scheduler.Deleted.ShouldBe([harness.Scheduler.KeepAlives[0].JobId]);
		harness.States.Stored[Provider.Claude].KeepAlive!.JobId.ShouldBe(harness.Scheduler.KeepAlives[1].JobId);
	}

	[Fact]
	public async Task A_token_close_to_its_expiry_is_refreshed_and_the_keep_alive_follows_the_new_one()
	{
		var harness = new TestHarness();
		harness.Session.ExpiresAt = Start.AddMinutes(3);
		harness.Session.OnRefresh = _ => Start.AddHours(8);

		await harness.KeepAlive.Run(Token);

		harness.Session.Refreshes.ShouldBe(1);
		harness.Scheduler.KeepAlives.ShouldHaveSingleItem().RunAt.ShouldBe(Start.AddHours(8).AddMinutes(-4));
		harness.States.Stored[Provider.Claude].LastFailure.ShouldBeNull();
	}

	[Fact]
	public async Task A_cancelled_reading_is_neither_a_failure_nor_a_reading()
	{
		var harness = new TestHarness();
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		harness.CodexReader.Respond = () =>
		{
			// The job is stopping (service shutdown or deleted job) while the CLI runs.
			cancellation.Cancel();
			throw new OperationCanceledException(cancellation.Token);
		};

		await Should.ThrowAsync<OperationCanceledException>(() => harness.Monitor.Poll(Provider.Codex, cancellation.Token));

		harness.States.Stored.ShouldNotContainKey(Provider.Codex);
		harness.Snapshots.Added.ShouldBeEmpty();
		harness.Sender.Sent.ShouldBeEmpty();
	}

	private sealed class RecordingStorage : IStorageInitializer
	{
		public bool Initialized { get; private set; }

		public Task Initialize(CancellationToken cancellationToken)
		{
			Initialized = true;
			return Task.CompletedTask;
		}
	}
}
