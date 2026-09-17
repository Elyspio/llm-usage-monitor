using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class SettingsServiceTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Polling_outside_1_to_60_minutes_is_rejected_per_field()
	{
		var harness = new TestHarness();

		var exception = await Should.ThrowAsync<RequestValidationException>(() => harness.Settings.UpdatePolling(new(0, 61), Token));

		exception.Errors.Keys.ShouldBe(["claudeIntervalMinutes", "codexIntervalMinutes"], true);
	}

	[Fact]
	public async Task A_new_polling_interval_rewrites_the_poll_jobs_at_once()
	{
		var harness = new TestHarness();

		await harness.Settings.UpdatePolling(new(5, 1), Token);

		harness.Scheduler.PollIntervals[Provider.Claude].ShouldBe(5);
		harness.Scheduler.PollIntervals[Provider.Codex].ShouldBe(1);
		(await harness.Settings.Get(Token)).Polling.ShouldBe(new(5, 1));
	}

	[Fact]
	public async Task Disabling_the_automatic_trigger_cancels_the_pending_post_reset_check()
	{
		var harness = new TestHarness();
		await harness.States.Save(new(Provider.Codex) { PendingResetCheck = new("job-42", TestHarness.Start.AddHours(1)) }, Token);

		await harness.Settings.UpdateTriggers(new(new(true, "haiku"), new(false, " gpt-5.6-luna ")), Token);

		harness.Scheduler.Deleted.ShouldBe(["job-42"]);
		harness.States.Stored[Provider.Codex].PendingResetCheck.ShouldBeNull();
		(await harness.Settings.Get(Token)).Triggers.Codex.Model.ShouldBe("gpt-5.6-luna");
	}

	[Fact]
	public async Task An_empty_model_is_rejected()
	{
		var harness = new TestHarness();

		var exception = await Should.ThrowAsync<RequestValidationException>(() =>
			harness.Settings.UpdateTriggers(new(new(true, " "), new(true, "luna")), Token));

		exception.Errors.Keys.ShouldBe(["claude.model"]);
	}

	[Fact]
	public async Task The_ntfy_token_is_encrypted_kept_or_removed_and_never_returned()
	{
		var harness = new TestHarness();
		var events = NotificationEventsByProvider.Default;

		var set = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "secret", events, 3), Token);
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
		var kept = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", null, events, 3), Token);
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
		var removed = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "", events, 3), Token);

		(set.TokenDefined, kept.TokenDefined, removed.TokenDefined).ShouldBe((true, true, false));
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBeNull();
	}

	[Fact]
	public async Task Invalid_notification_settings_are_rejected_per_field()
	{
		var harness = new TestHarness();

		var exception = await Should.ThrowAsync<RequestValidationException>(() =>
			harness.Settings.UpdateNotifications(new("ftp://ntfy", "bad topic!", null, NotificationEventsByProvider.Default, 21), Token));

		exception.Errors.Keys.ShouldBe(["url", "topic", "readFailureThreshold"], true);
	}

	[Fact]
	public async Task A_delivery_failure_is_recorded_instead_of_thrown()
	{
		var harness = new TestHarness();
		harness.Sender.Failure = new HttpRequestException("ntfy returned HTTP 502.");

		await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "boom", Token);

		harness.SettingsRepository.Stored!.Notifications.LastSendFailure!.Message.ShouldBe("ntfy returned HTTP 502.");
	}

	[Fact]
	public async Task Notification_events_are_enabled_per_provider()
	{
		var harness = new TestHarness();
		var settings = harness.SettingsRepository.Stored!;
		harness.SettingsRepository.Stored = settings with
		{
			Notifications = settings.Notifications with
			{
				Events = new(NotificationEvents.Default, NotificationEvents.Default with { TriggerFailed = false })
			}
		};

		await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "ignored", Token);
		await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Claude, "sent", Token);

		harness.Sender.Sent.ShouldHaveSingleItem().Title.ShouldStartWith("Claude");
	}
}

public sealed class TriggerServiceTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_manual_trigger_is_queued_then_run_without_notification()
	{
		var harness = new TestHarness();

		var run = await harness.Triggers.RequestManual(Provider.Codex, Token);
		harness.Scheduler.EnqueuedTriggers.ShouldBe([run.Id]);
		await harness.Triggers.ExecuteManual(run.Id, Token);

		var completed = await harness.Triggers.Get(run.Id, Token);
		completed.Manual.ShouldBeTrue();
		completed.Status.ShouldBe(TriggerStatus.Succeeded);
		harness.CodexRunner.Models.ShouldBe(["gpt-5.6-luna"]);
		harness.Sender.Sent.ShouldBeEmpty();
	}

	[Fact]
	public async Task A_second_manual_trigger_while_one_runs_is_refused_as_busy()
	{
		var harness = new TestHarness();
		await harness.Triggers.RequestManual(Provider.Claude, Token);

		var exception = await Should.ThrowAsync<ProviderException>(() => harness.Triggers.RequestManual(Provider.Claude, Token));

		exception.Code.ShouldBe(ProviderErrorCodes.CliBusy);
	}

	[Fact]
	public async Task The_dashboard_shows_every_provider_with_its_settings_and_recent_runs()
	{
		var harness = new TestHarness();
		await harness.Triggers.RequestManual(Provider.Codex, Token);
		var dashboard = new DashboardService(harness.States, harness.Runs, harness.Settings, harness.Time);

		var snapshot = await dashboard.GetDashboard(Token);

		snapshot.Providers.Select(provider => provider.Provider).ShouldBe([Provider.Claude, Provider.Codex]);
		snapshot.Providers[1].RunningTrigger.ShouldNotBeNull();
		snapshot.Providers[0].PollIntervalMinutes.ShouldBe(3);
		snapshot.RecentTriggerRuns.ShouldHaveSingleItem();
	}
}
