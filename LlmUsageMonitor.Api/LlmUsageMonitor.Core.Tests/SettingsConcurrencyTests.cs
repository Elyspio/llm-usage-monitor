using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class SettingsConcurrencyTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_settings_save_concurrent_with_a_notification_failure_keeps_both()
	{
		var harness = new TestHarness();
		harness.Sender.Failure = new HttpRequestException("ntfy returned HTTP 502.");
		// The delivery fails between the read and the write of the settings save.
		harness.SettingsRepository.AfterNextFind = () => harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "boom", Token);

		await harness.Settings.UpdateNotifications(new("https://ntfy.example", "topic_2", null, NotificationEventsByProvider.Default, 5), Token);

		var saved = harness.SettingsRepository.Stored!.Notifications;
		(saved.Url, saved.Topic, saved.ReadFailureThreshold).ShouldBe(("https://ntfy.example", "topic_2", 5));
		saved.LastSendFailure!.Message.ShouldBe("ntfy returned HTTP 502.");
	}

	[Fact]
	public async Task A_polling_save_concurrent_with_a_notification_failure_keeps_both()
	{
		var harness = new TestHarness();
		harness.Sender.Failure = new HttpRequestException("ntfy returned HTTP 502.");
		harness.SettingsRepository.AfterNextFind = () => harness.Settings.UpdatePolling(new(10, 10), Token);

		await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "boom", Token);

		var saved = harness.SettingsRepository.Stored!;
		saved.Polling.ShouldBe(new(10, 10));
		saved.Notifications.LastSendFailure.ShouldNotBeNull();
	}

	[Fact]
	public async Task Disabling_the_automatic_trigger_waits_for_the_provider_lock_before_writing_the_state()
	{
		var harness = new TestHarness();
		await harness.States.Save(new(Provider.Codex) { PendingResetCheck = new("job-42", TestHarness.Start.AddHours(1)) }, Token);
		var held = await harness.Locks.TryAcquire(Provider.Codex, TimeSpan.Zero, Token);

		var update = harness.Settings.UpdateTriggers(new(new(true, "haiku"), new(false, "luna")), Token);
		harness.Scheduler.Deleted.ShouldBeEmpty();
		held!.Dispose();
		await update;

		harness.Scheduler.Deleted.ShouldBe(["job-42"]);
		harness.States.Stored[Provider.Codex].PendingResetCheck.ShouldBeNull();
	}

	[Fact]
	public async Task A_check_left_by_a_busy_provider_is_dropped_by_the_next_reading()
	{
		var harness = new TestHarness();
		await harness.States.Save(new(Provider.Codex) { PendingResetCheck = new("job-42", TestHarness.Start.AddHours(1)) }, Token);
		using (await harness.Locks.TryAcquire(Provider.Codex, TimeSpan.Zero, Token))
		{
			var update = harness.Settings.UpdateTriggers(new(new(true, "haiku"), new(false, "luna")), Token);
			harness.Time.Advance(SettingsService.ProviderWait);
			await update;
		}

		harness.States.Stored[Provider.Codex].PendingResetCheck.ShouldNotBeNull();
		harness.CodexReader.Respond = () => [TestHarness.Window("codex/primary", 40, TestHarness.Start.AddMinutes(50))];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Scheduler.Deleted.ShouldBe(["job-42"]);
		harness.States.Stored[Provider.Codex].PendingResetCheck.ShouldBeNull();
	}
}
