using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class ResetCreditTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;
	private static ResetCredit Credit(string id, int minutes = 30, int uses = 1) => new(id, uses, TestHarness.Start.AddMinutes(minutes), TestHarness.Start.AddDays(-2), "Full reset", true, false, []);
	private static TestHarness Harness(bool enabled = true)
	{
		var h = new TestHarness();
		h.SettingsRepository.Stored = h.SettingsRepository.Stored! with { ResetCredits = new(new(false, 60), new(enabled, 60)) };
		h.CodexReader.Respond = () => [TestHarness.Window("codex/primary", 0, null)];
		return h;
	}

	[Fact]
	public async Task Every_due_credit_is_attempted_in_expiry_order_even_with_zero_usage_and_without_a_prompt()
	{
		var h = Harness();
		h.CodexReader.Credits = new(3, [Credit("later", 50), Credit("first", 10), Credit("future", 120)]);
		h.CodexConsumer.Respond = (id, _) =>
		{
			h.CodexReader.Credits = new(h.CodexReader.Credits!.AvailableCount - 1, h.CodexReader.Credits.Credits!.Where(credit => credit.Id != id).ToList());
			return new(true, "reset");
		};
		await h.Monitor.Poll(Provider.Codex, Token);
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.Select(call => call.CreditId).ShouldBe(["first", "later"]);
		h.CodexRunner.Models.ShouldBeEmpty();
		h.CreditRuns.All.ShouldAllBe(run => run.Status == ResetCreditRunStatus.Succeeded);
		h.Sender.Sent.Count(message => message.Title.Contains("crédit de reset utilisé")).ShouldBe(2);
	}

	[Fact]
	public async Task Unknown_expiry_and_ineligible_grants_are_not_automatically_redeemed()
	{
		var h = Harness();
		h.CodexReader.Credits = new(3, [Credit("undated") with { ExpiresAt = null }, Credit("locked") with { IsUsable = false }, Credit("expired", -1)]);
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.ShouldBeEmpty();
	}

	[Fact]
	public async Task Count_only_data_never_invents_a_credit_to_consume()
	{
		var h = Harness();
		h.CodexReader.Credits = new(12, null);
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.ShouldBeEmpty();
		(await h.States.Get(Provider.Codex, Token)).ResetCredits!.AvailableCount.ShouldBe(12);
	}

	[Fact]
	public async Task Manual_redemption_is_available_with_automation_off_and_http_replays_do_not_consume_twice()
	{
		var h = Harness(false);
		h.CodexReader.Credits = new(1, [Credit("undated") with { ExpiresAt = null }]);
		var request = new ConsumeResetCreditRequest("undated", Guid.NewGuid().ToString());
		var first = await h.Credits.ConsumeManual(Provider.Codex, request, Token);
		(await h.Credits.ConsumeManual(Provider.Codex, request, Token)).ShouldBe(first);
		h.CodexConsumer.Calls.Count.ShouldBe(1);
		h.CodexRunner.Models.ShouldBeEmpty();
	}

	[Fact]
	public async Task A_lost_reply_is_retried_with_the_persisted_key_even_after_restart()
	{
		var h = Harness();
		h.CodexReader.Credits = new(1, [Credit("retry")]);
		h.CodexConsumer.Respond = (_, _) => throw new ProviderException(ProviderErrorCodes.Timeout, "lost reply");
		await h.Monitor.Poll(Provider.Codex, Token);
		var pending = h.CreditRuns.All.ShouldHaveSingleItem();
		pending.NextRetryAt.ShouldBe(TestHarness.Start.AddMinutes(2));
		h.Time.Advance(TimeSpan.FromMinutes(2));
		h.CodexConsumer.Respond = (_, _) => { h.CodexReader.Credits = new(0, []); return new(true, "alreadyRedeemed"); };
		// Polling startup resumes records, not in-memory attempt state.
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.Select(call => call.Key).ShouldBe([pending.Id, pending.Id]);
		h.CreditRuns.All.ShouldHaveSingleItem().Status.ShouldBe(ResetCreditRunStatus.Succeeded);
		h.CodexRunner.Models.ShouldBeEmpty();
	}

	[Fact]
	public async Task Refused_credit_does_not_prevent_attempting_the_next_expiring_credit_or_loop_every_poll()
	{
		var h = Harness();
		h.CodexReader.Credits = new(2, [Credit("one"), Credit("two")]);
		h.CodexConsumer.Respond = (_, _) => new(false, "nothingToReset");
		await h.Monitor.Poll(Provider.Codex, Token);
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.Count.ShouldBe(2);
		h.CreditRuns.All.ShouldAllBe(run => run.Status == ResetCreditRunStatus.Refused);
	}

	[Fact]
	public async Task A_grant_with_several_uses_is_consumed_with_a_new_key_per_use()
	{
		var h = Harness();
		h.CodexReader.Credits = new(3, [Credit("grant", uses: 3)]);
		h.CodexConsumer.Respond = (_, _) => { var left = h.CodexReader.Credits!.AvailableCount!.Value - 1; h.CodexReader.Credits = new(left, [Credit("grant", uses: left)]); return new(true, "reset"); };
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.Count.ShouldBe(3);
		h.CodexConsumer.Calls.Select(call => call.Key).Distinct().Count().ShouldBe(3);
	}

	[Fact]
	public async Task An_automatic_reset_is_off_by_default_and_invalid_deadlines_are_rejected()
	{
		var h = new TestHarness();
		(await h.Settings.Get(Token)).ResetCredits.ShouldBe(ResetCreditSettings.Default);
		await Should.ThrowAsync<RequestValidationException>(() => h.Settings.UpdateResetCredits(new(new(true, 0), new(false, 60)), Token));
	}

	[Fact]
	public async Task The_deadline_schedules_a_read_even_when_the_poll_interval_is_longer()
	{
		var h = Harness();
		h.CodexReader.Credits = new(1, [Credit("future", 90)]);
		await h.Monitor.Poll(Provider.Codex, Token);
		h.Scheduler.TriggerRetries.ShouldContain((Provider.Codex, TestHarness.Start.AddMinutes(30)));
		h.CodexConsumer.Calls.ShouldBeEmpty();
	}

	[Fact]
	public async Task The_dashboard_skips_unusable_and_already_attempted_automatic_credits()
	{
		var h = Harness();
		h.CodexReader.Credits = new(3, [Credit("locked", 5) with { IsUsable = false }, Credit("refused", 10), Credit("next", 90)]);
		h.CodexConsumer.Respond = (_, _) => new(false, "nothingToReset");
		await h.Monitor.Poll(Provider.Codex, Token);
		// The selection remains truthful even when the attempt has fallen out of the displayed history.
		for (var index = 0; index < 10; index++)
			await h.CreditRuns.Start(new(Guid.NewGuid().ToString(), Provider.Codex, "old", true, null, TestHarness.Start.AddSeconds(index + 1), null, ResetCreditRunStatus.Succeeded), Token);
		var dashboard = await new DashboardService(h.States, h.Runs, h.Settings, h.Time, h.CreditRuns).GetDashboard(Token);
		var credits = dashboard.Providers.Single(provider => provider.Provider == Provider.Codex).ResetCredits!;
		credits.NextCreditId.ShouldBe("next");
		credits.NextAttemptAt.ShouldBe(TestHarness.Start.AddMinutes(30));
	}

	[Fact]
	public async Task Manual_confirmation_resumes_a_paused_automatic_request_using_its_original_key()
	{
		var h = Harness();
		h.CodexReader.Credits = new(1, [Credit("retry")]);
		h.CodexConsumer.Respond = (_, _) => new(false, "unavailable", true);
		await h.Monitor.Poll(Provider.Codex, Token);
		var pending = h.CreditRuns.All.ShouldHaveSingleItem();
		await h.Settings.UpdateResetCredits(ResetCreditSettings.Default, Token);
		h.Time.Advance(TimeSpan.FromMinutes(2));
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CodexConsumer.Calls.Count.ShouldBe(1);
		h.CodexConsumer.Respond = (_, _) => new(true, "alreadyRedeemed");
		await Should.ThrowAsync<ProviderException>(() => h.Credits.ConsumeManual(Provider.Codex, new("retry", Guid.NewGuid().ToString()), Token));
		var request = new ConsumeResetCreditRequest("retry", pending.Id);
		var result = await h.Credits.ConsumeManual(Provider.Codex, request, Token);
		result.Status.ShouldBe(ResetCreditRunStatus.Succeeded);
		result.Manual.ShouldBeTrue();
		(await h.Credits.ConsumeManual(Provider.Codex, request, Token)).ShouldBe(result);
		h.CodexConsumer.Calls.Select(call => call.Key).ShouldBe([pending.Id, pending.Id]);
	}

	[Fact]
	public async Task An_expired_pending_request_is_closed_even_when_automation_is_paused()
	{
		var h = Harness();
		h.CodexReader.Credits = new(1, [Credit("retry", 3)]);
		h.CodexConsumer.Respond = (_, _) => new(false, "unavailable", true);
		await h.Monitor.Poll(Provider.Codex, Token);
		await h.Settings.UpdateResetCredits(ResetCreditSettings.Default, Token);
		h.Time.Advance(TimeSpan.FromMinutes(4));
		await h.Monitor.Poll(Provider.Codex, Token);
		h.CreditRuns.All.ShouldHaveSingleItem().Status.ShouldBe(ResetCreditRunStatus.Failed);
		h.CodexConsumer.Calls.Count.ShouldBe(1);
	}
}
