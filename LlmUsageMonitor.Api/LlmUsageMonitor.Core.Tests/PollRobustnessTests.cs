using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class PollRobustnessTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_reset_seen_again_after_an_interrupted_poll_is_stored_and_notified_once()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.Advance(TimeSpan.FromMinutes(3));
		harness.CodexReader.Respond = () => [Window("codex/primary", 5, Start.AddHours(5))];

		// The storage fails after the reset is stored: neither the state nor the failure can be saved.
		harness.States.FailingSaves = 2;
		await Should.ThrowAsync<TimeoutException>(() => harness.Monitor.Poll(Provider.Codex, Token));
		harness.Time.Advance(TimeSpan.FromMinutes(3));
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Resets.Added.ShouldHaveSingleItem();
		harness.Sender.Sent.Count(message => message.Title == "Codex : reset détecté").ShouldBe(1);
		harness.States.Stored[Provider.Codex].LastReading!.Windows.ShouldHaveSingleItem().UsedPercent.ShouldBe(5);
	}

	[Fact]
	public async Task An_unexpected_exception_updates_the_health_of_the_provider()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => throw new InvalidOperationException("No process is associated with this object.");

		for (var i = 0; i < 3; i++) await harness.Monitor.Poll(Provider.Codex, Token);

		var state = harness.States.Stored[Provider.Codex];
		state.LastFailure!.Code.ShouldBe(ProviderErrorCode.UnexpectedError);
		state.ConsecutiveFailures.ShouldBe(3);
		harness.Sender.Sent.ShouldHaveSingleItem().Title.ShouldBe("Codex : lectures en échec");
	}

	[Fact]
	public async Task A_storage_failure_during_the_poll_is_recorded_as_a_failed_reading()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 10, Start.AddHours(1))];
		harness.States.FailingSaves = 1;

		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.States.Stored[Provider.Codex].LastFailure!.Code.ShouldBe(ProviderErrorCode.UnexpectedError);
	}

	[Fact]
	public async Task The_post_reset_check_rescheduling_itself_does_not_delete_its_own_job()
	{
		var harness = new TestHarness();
		var resetsAt = Start.AddMinutes(30);
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, resetsAt)];
		await harness.Monitor.Poll(Provider.Codex, Token);
		var check = harness.Scheduler.PostResetChecks.ShouldHaveSingleItem();

		// The check runs: the window has reset and already announces its next reset.
		harness.Time.SetUtcNow(check.RunAt);
		harness.CodexReader.Respond = () => [Window("codex/primary", 1, check.RunAt.AddHours(5))];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Scheduler.Deleted.ShouldBeEmpty();
		harness.Scheduler.PostResetChecks.Count.ShouldBe(2);
		harness.States.Stored[Provider.Codex].PendingResetCheck!.RunAt.ShouldBe(check.RunAt.AddHours(5).AddMinutes(1));
	}

	[Fact]
	public async Task A_post_reset_check_that_ran_is_cleared_from_the_state()
	{
		var harness = new TestHarness();
		var resetsAt = Start.AddMinutes(30);
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, resetsAt)];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Time.SetUtcNow(resetsAt.AddMinutes(1));
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.States.Stored[Provider.Codex].PendingResetCheck.ShouldBeNull();
		harness.Scheduler.Deleted.ShouldBeEmpty();
	}

	[Fact]
	public async Task The_running_keep_alive_does_not_delete_its_own_job()
	{
		var harness = new TestHarness();
		var runAt = Start.AddHours(8).AddMinutes(-4);
		await harness.States.Save(new(Provider.Claude) { KeepAlive = new("job-keep-alive", runAt), TokenExpiresAt = Start.AddHours(8) }, Token);
		harness.Time.SetUtcNow(runAt);
		harness.Session.ExpiresAt = Start.AddHours(8);
		harness.Session.OnRefresh = _ => Start.AddHours(16);

		await harness.KeepAlive.Run(Token);

		harness.Scheduler.Deleted.ShouldBeEmpty();
		harness.States.Stored[Provider.Claude].KeepAlive!.RunAt.ShouldBe(Start.AddHours(16).AddMinutes(-4));
	}

	[Fact]
	public async Task A_reading_is_dated_when_it_is_received()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () =>
		{
			harness.Time.Advance(TimeSpan.FromSeconds(90));
			return [Window("codex/primary", 10, Start.AddHours(1))];
		};

		await harness.Monitor.Poll(Provider.Codex, Token);

		var state = harness.States.Stored[Provider.Codex];
		state.LastReading!.FetchedAt.ShouldBe(Start.AddSeconds(90));
		state.LastSuccessAt.ShouldBe(Start.AddSeconds(90));
	}
}
