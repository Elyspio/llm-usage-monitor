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
	public async Task Polling_that_does_not_divide_the_hour_is_rejected()
	{
		var harness = new TestHarness();

		var exception = await Should.ThrowAsync<RequestValidationException>(() => harness.Settings.UpdatePolling(new(45, 7), Token));

		exception.Errors.Keys.ShouldBe(["claudeIntervalMinutes", "codexIntervalMinutes"], true);
		exception.Errors["claudeIntervalMinutes"].ShouldBe([SettingsService.IntervalDivisorMessage]);
	}

	[Theory]
	[InlineData(45, 30)]
	[InlineData(7, 6)]
	[InlineData(15, 15)]
	[InlineData(0, 1)]
	[InlineData(90, 60)]
	public void A_saved_interval_is_rounded_down_to_a_divisor_of_the_hour(int saved, int expected)
	{
		PollingSettings.ToHourDivisor(saved).ShouldBe(expected);
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

		var set = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "secret", events, 3, 7), Token);
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
		var kept = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", null, events, 3, 7), Token);
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
		var removed = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "", events, 3, 7), Token);

		(set.TokenDefined, kept.TokenDefined, removed.TokenDefined).ShouldBe((true, true, false));
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBeNull();
	}

	[Fact]
	public async Task A_new_ntfy_server_does_not_inherit_the_stored_token()
	{
		var harness = new TestHarness();
		var events = NotificationEventsByProvider.Default;
		await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "secret", events, 3, 7), Token);

		var exception = await Should.ThrowAsync<RequestValidationException>(() =>
			harness.Settings.UpdateNotifications(new("https://ntfy.example.org", "topic_1", null, events, 3, 7), Token));

		exception.Errors.Keys.ShouldBe(["token"]);
		harness.SettingsRepository.Stored!.Notifications.Url.ShouldBe("https://ntfy.sh");
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
	}

	[Fact]
	public async Task A_new_ntfy_server_is_saved_with_its_own_token_or_none()
	{
		var harness = new TestHarness();
		var events = NotificationEventsByProvider.Default;
		await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "secret", events, 3, 7), Token);

		var reentered = await harness.Settings.UpdateNotifications(new("https://ntfy.example.org", "topic_1", "other", events, 3, 7), Token);
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:other");
		var removed = await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "", events, 3, 7), Token);

		(reentered.TokenDefined, removed.TokenDefined).ShouldBe((true, false));
		harness.SettingsRepository.Stored!.Notifications.Url.ShouldBe("https://ntfy.sh");
	}

	[Fact]
	public async Task The_same_ntfy_server_written_differently_keeps_the_token()
	{
		var harness = new TestHarness();
		var events = NotificationEventsByProvider.Default;
		await harness.Settings.UpdateNotifications(new("https://ntfy.sh", "topic_1", "secret", events, 3, 7), Token);

		var kept = await harness.Settings.UpdateNotifications(new(" https://NTFY.sh/ ", "topic_1", null, events, 3, 7), Token);

		kept.TokenDefined.ShouldBeTrue();
		harness.SettingsRepository.Stored!.Notifications.ProtectedToken.ShouldBe("protected:secret");
	}

	[Theory]
	[InlineData("https://ntfy.example.org/Team", "https://ntfy.example.org/team")]
	[InlineData("https://ntfy.example.org/team", "https://ntfy.example.org/team?Key=1")]
	[InlineData("https://ntfy.example.org", "http://ntfy.example.org")]
	[InlineData("https://ntfy.example.org", "https://ntfy.example.org:8443")]
	public async Task A_different_path_query_scheme_or_port_is_another_server(string saved, string updated)
	{
		var harness = new TestHarness();
		var events = NotificationEventsByProvider.Default;
		await harness.Settings.UpdateNotifications(new(saved, "topic_1", "secret", events, 3, 7), Token);

		var exception = await Should.ThrowAsync<RequestValidationException>(() => harness.Settings.UpdateNotifications(new(updated, "topic_1", null, events, 3, 7), Token));

		exception.Errors.Keys.ShouldBe(["token"]);
	}

	[Fact]
	public async Task A_cli_error_is_sent_short_and_without_credentials()
	{
		var harness = new TestHarness();
		var error = "TRIGGER_FAILED : request failed\n\tAuthorization: Bearer abc.def.ghi for user@example.com, refresh_token=rt_0123456789 "
		            + "key sk-ant-oat01-ABCDEFGHIJKLMNOPQRSTUV " + string.Join(" ", Enumerable.Repeat("at stack frame", 40));

		await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Claude, error, Token);

		var body = harness.Sender.Sent.ShouldHaveSingleItem().Body;
		body.Length.ShouldBeLessThanOrEqualTo(NotificationDetail.MaxLength);
		body.ShouldStartWith("TRIGGER_FAILED : request failed Authorization: [redacted]");
		body.ShouldNotContain("abc.def.ghi");
		body.ShouldNotContain("user@example.com");
		body.ShouldNotContain("rt_0123456789");
		body.ShouldNotContain("sk-ant-");
		body.ShouldNotContain("\n");
		body.ShouldEndWith("…");
	}

	[Theory]
	[InlineData("Prompt envoyé : un nouveau cycle est ouvert.", "Prompt envoyé : un nouveau cycle est ouvert.")]
	[InlineData("AUTH_EXPIRED : refresh token expired, run `claude auth login`", "AUTH_EXPIRED : refresh token expired, run `claude auth login`")]
	[InlineData("CLI_EXITED : /var/lib/llm-monitor/.local/bin/claude exited with 1", "CLI_EXITED : /var/lib/llm-monitor/.local/bin/claude exited with 1")]
	[InlineData("HTTP_ERROR : 401 with eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", "HTTP_ERROR : 401 with [redacted]")]
	[InlineData("FETCH_FAILED : {\"access_token\": \"abc\", \"api_key\":\"def\"}", "FETCH_FAILED : {\"access_token\": \"[redacted]\", \"api_key\":\"[redacted]\"}")]
	[InlineData("INVALID_RESPONSE : 0123456789abcdef0123456789abcdef01234567", "INVALID_RESPONSE : [redacted]")]
	public void A_notification_detail_keeps_the_message_and_drops_the_secrets(string detail, string expected)
	{
		NotificationDetail.Sanitize(detail).ShouldBe(expected);
	}

	[Fact]
	public async Task Invalid_notification_settings_are_rejected_per_field()
	{
		var harness = new TestHarness();

		var exception = await Should.ThrowAsync<RequestValidationException>(() =>
			harness.Settings.UpdateNotifications(new("ftp://ntfy", "bad topic!", null, NotificationEventsByProvider.Default, 21, 0), Token));

		exception.Errors.Keys.ShouldBe(["url", "topic", "readFailureThreshold", "credentialExpiryAlertDays"], true);
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
