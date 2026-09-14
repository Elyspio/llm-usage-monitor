using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class UsageMonitorTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_reading_is_stored_and_kept_as_the_last_valid_one()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 11, Start.AddDays(5), 10_080)];

		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Snapshots.Added.ShouldHaveSingleItem().Reading.Windows.ShouldHaveSingleItem().UsedPercent.ShouldBe(11);
		harness.States.Stored[Provider.Codex].LastReading!.FetchedAt.ShouldBe(Start);
	}

	[Fact]
	public async Task A_reading_before_the_reset_schedules_a_check_one_minute_after_it()
	{
		var harness = new TestHarness();
		var resetsAt = Start.AddHours(2);
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, resetsAt)];

		await harness.Monitor.Poll(Provider.Codex, Token);
		await harness.Monitor.Poll(Provider.Codex, Token);

		var check = harness.Scheduler.PostResetChecks.ShouldHaveSingleItem();
		check.RunAt.ShouldBe(resetsAt.AddMinutes(1));
		harness.States.Stored[Provider.Codex].PendingResetCheck!.JobId.ShouldBe(check.JobId);
	}

	[Fact]
	public async Task The_trigger_window_back_to_zero_triggers_once_per_cycle()
	{
		var harness = new TestHarness();
		var resetsAt = Start.AddMinutes(30);
		harness.ClaudeReader.Respond = () => [Window("five_hour", 40, resetsAt), Window("seven_day", 20, Start.AddDays(3), 10_080)];
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Time.SetUtcNow(resetsAt.AddMinutes(1));
		harness.ClaudeReader.Respond = () => [Window("five_hour", 0, null), Window("seven_day", 20, Start.AddDays(3), 10_080)];
		await harness.Monitor.Poll(Provider.Claude, Token);
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.ClaudeRunner.Models.ShouldBe(["haiku"]);
		var run = harness.Runs.All.ShouldHaveSingleItem();
		run.Manual.ShouldBeFalse();
		run.Status.ShouldBe(TriggerStatus.Succeeded);
		run.CycleKey.ShouldBe($"resets:{resetsAt:yyyy-MM-ddTHH:mm}Z");
		harness.Sender.Sent.ShouldContain(message => message.Title == "Claude : nouveau cycle ouvert");
	}

	[Fact]
	public async Task A_window_started_after_the_trigger_opens_no_new_cycle()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.SetUtcNow(Start.AddMinutes(11));
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];
		await harness.Monitor.Poll(Provider.Codex, Token);

		// The prompt costs less than 1 %: the window shows 0 % but now announces its reset time.
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, Start.AddMinutes(11).AddHours(5))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Runs.All.ShouldHaveSingleItem();
		harness.States.Stored[Provider.Codex].CurrentCycleKey.ShouldBeNull();
	}

	[Fact]
	public async Task A_failed_automatic_trigger_is_notified_and_never_retried()
	{
		var harness = new TestHarness();
		harness.CodexRunner.Failure = new ProviderException(ProviderErrorCodes.UsageLimit, "You've hit your limit");
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.SetUtcNow(Start.AddMinutes(11));
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];

		await harness.Monitor.Poll(Provider.Codex, Token);
		await harness.Monitor.Poll(Provider.Codex, Token);

		var run = harness.Runs.All.ShouldHaveSingleItem();
		run.Status.ShouldBe(TriggerStatus.Failed);
		run.ErrorCode.ShouldBe(ProviderErrorCodes.UsageLimit);
		harness.CodexRunner.Models.Count.ShouldBe(1);
		harness.Sender.Sent.Count(message => message.Title == "Codex : déclenchement échoué").ShouldBe(1);
	}

	[Fact]
	public async Task Starting_on_an_unused_window_does_not_trigger_on_its_own()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];

		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Runs.All.ShouldBeEmpty();
	}

	[Fact]
	public async Task With_the_automatic_trigger_disabled_nothing_is_scheduled_nor_triggered()
	{
		var harness = new TestHarness(autoTriggerEnabled: false);
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.SetUtcNow(Start.AddMinutes(11));
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];

		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Scheduler.PostResetChecks.ShouldBeEmpty();
		harness.Runs.All.ShouldBeEmpty();
	}

	[Fact]
	public async Task A_reset_is_recorded_and_notified()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.CodexReader.Respond = () => [Window("codex/primary", 5, Start.AddHours(5))];

		await harness.Monitor.Poll(Provider.Codex, Token);

		var reset = harness.Resets.Added.ShouldHaveSingleItem();
		(reset.UsedPercentBefore, reset.UsedPercentAfter).ShouldBe((40, 5));
		harness.Sender.Sent.ShouldContain(message => message.Title == "Codex : reset détecté");
	}

	[Fact]
	public async Task Rate_limited_readings_back_off_15_30_then_60_minutes()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => throw new ProviderException(ProviderErrorCodes.RateLimited, "HTTP 429");

		var backoffs = new List<TimeSpan>();
		for (var i = 0; i < 4; i++)
		{
			await harness.Monitor.Poll(Provider.Codex, Token);
			var until = harness.States.Stored[Provider.Codex].BackoffUntil!.Value;
			backoffs.Add(until - harness.Time.GetUtcNow());

			await harness.Monitor.Poll(Provider.Codex, Token); // skipped: still backing off
			harness.Time.SetUtcNow(until);
		}

		backoffs.ShouldBe([TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60)]);
		harness.CodexReader.Calls.ShouldBe(4);
		harness.States.Stored[Provider.Codex].ConsecutiveFailures.ShouldBe(0);
	}

	[Fact]
	public async Task Read_failures_alert_once_after_the_threshold_then_notify_the_recovery()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => throw new ProviderException(ProviderErrorCodes.FetchFailed, "offline");
		for (var i = 0; i < 5; i++) await harness.Monitor.Poll(Provider.Codex, Token);

		harness.CodexReader.Respond = () => [Window("codex/primary", 10, Start.AddHours(1))];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Sender.Sent.Select(message => message.Title).ShouldBe(["Codex : lectures en échec", "Codex : rétabli"]);
		var state = harness.States.Stored[Provider.Codex];
		state.ConsecutiveFailures.ShouldBe(0);
		state.ActiveAlerts.ShouldBeEmpty();
		state.LastFailure!.Code.ShouldBe(ProviderErrorCodes.FetchFailed);
	}

	[Fact]
	public async Task An_empty_reading_is_a_failure()
	{
		var harness = new TestHarness();

		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.States.Stored[Provider.Codex].LastFailure!.Code.ShouldBe(ProviderErrorCodes.NoUsageData);
	}

	[Fact]
	public async Task A_Claude_token_about_to_expire_is_refreshed_through_the_cli_before_reading()
	{
		var harness = new TestHarness();
		harness.Session.ExpiresAt = Start.AddMinutes(2);
		harness.Session.OnRefresh = _ => Start.AddHours(8);
		harness.ClaudeReader.Respond = () => [Window("five_hour", 2, Start.AddHours(4))];

		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Session.Refreshes.ShouldBe(1);
		harness.ClaudeReader.Calls.ShouldBe(1);
		harness.Scheduler.KeepAlives.ShouldHaveSingleItem().RunAt.ShouldBe(Start.AddHours(8).AddMinutes(-4));
		harness.States.Stored[Provider.Claude].TokenExpiresAt.ShouldBe(Start.AddHours(8));
	}

	[Fact]
	public async Task A_Claude_login_that_cannot_be_refreshed_raises_one_auth_expired_alert()
	{
		var harness = new TestHarness();
		harness.Session.ExpiresAt = Start.AddMinutes(-1);

		await harness.Monitor.Poll(Provider.Claude, Token);
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.ClaudeReader.Calls.ShouldBe(0);
		harness.Session.Refreshes.ShouldBe(4);
		harness.States.Stored[Provider.Claude].LastFailure!.Code.ShouldBe(ProviderErrorCodes.AuthExpired);
		harness.Sender.Sent.ShouldHaveSingleItem().Title.ShouldBe("Claude : connexion expirée");
	}
}
