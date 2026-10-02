using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class NotificationDeliveryTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>What an <c>HttpClient</c> timeout throws: a cancellation the caller never asked for.</summary>
	private static TaskCanceledException HttpTimeout()
	{
		return new("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing.", new TimeoutException());
	}

	[Fact]
	public async Task A_ntfy_timeout_during_a_poll_is_recorded_and_the_poll_completes()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, Start.AddMinutes(10))];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Sender.Failure = HttpTimeout();
		harness.CodexReader.Respond = () => [Window("codex/primary", 5, Start.AddHours(5))];

		await harness.Monitor.Poll(Provider.Codex, Token);
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Resets.Added.ShouldHaveSingleItem();
		harness.States.Stored[Provider.Codex].LastReading!.Windows.ShouldHaveSingleItem().UsedPercent.ShouldBe(5);
		harness.SettingsRepository.Stored!.Notifications.LastSendFailure!.Message.ShouldContain("HttpClient.Timeout");
	}

	[Fact]
	public async Task A_ntfy_timeout_on_the_test_message_is_a_delivery_failure()
	{
		var harness = new TestHarness();
		harness.Sender.Failure = HttpTimeout();

		var exception = await Should.ThrowAsync<ProviderException>(() => harness.Notifications.SendTest(Token));

		exception.Code.ShouldBe(NotificationService.DeliveryFailedCode);
		harness.SettingsRepository.Stored!.Notifications.LastSendFailure.ShouldNotBeNull();
	}

	[Fact]
	public async Task The_cancellation_of_the_caller_is_not_a_delivery_failure()
	{
		var harness = new TestHarness();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		harness.Sender.Failure = new OperationCanceledException(cancellation.Token);

		await Should.ThrowAsync<OperationCanceledException>(() => harness.Notifications.Notify(NotificationKind.ReadFailed, Provider.Codex, "x", cancellation.Token));
	}

	[Fact]
	public async Task An_undelivered_alert_stays_inactive_and_is_sent_at_the_next_poll()
	{
		var harness = new TestHarness();
		harness.Session.ExpiresAt = Start.AddMinutes(-1);
		harness.Sender.Failure = HttpTimeout();

		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.States.Stored[Provider.Claude].ActiveAlerts.ShouldBeEmpty();

		harness.Sender.Failure = null;
		await harness.Monitor.Poll(Provider.Claude, Token);
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.States.Stored[Provider.Claude].ActiveAlerts.ShouldBe([NotificationKind.AuthExpired]);
		harness.Sender.Sent.ShouldHaveSingleItem().Title.ShouldBe("Claude : connexion expirée");
	}

	[Fact]
	public async Task A_successful_delivery_clears_the_last_send_failure()
	{
		var harness = new TestHarness();
		harness.Sender.Failure = new HttpRequestException("ntfy returned HTTP 502.");
		(await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "boom", Token)).ShouldBe(NotificationOutcome.Failed);
		harness.SettingsRepository.Stored!.Notifications.LastSendFailure.ShouldNotBeNull();

		harness.Sender.Failure = null;
		(await harness.Notifications.Notify(NotificationKind.TriggerFailed, Provider.Codex, "boom", Token)).ShouldBe(NotificationOutcome.Delivered);

		harness.SettingsRepository.Stored!.Notifications.LastSendFailure.ShouldBeNull();
	}
}
