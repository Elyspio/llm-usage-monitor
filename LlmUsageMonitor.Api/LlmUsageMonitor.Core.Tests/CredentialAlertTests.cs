using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class CredentialAlertTests
{
	private const string Expiring = "Claude : connexion bientôt expirée";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>A Claude login whose refresh token expires in 10 days; the access token is refreshed by the CLI as needed.</summary>
	private static TestHarness Claude()
	{
		var harness = new TestHarness();
		harness.Session.RefreshTokenExpiresAt = Start.AddDays(10);
		harness.Session.OnRefresh = _ => harness.Time.GetUtcNow().AddHours(8);
		harness.ClaudeReader.Respond = () => [Window("five_hour", 10, harness.Time.GetUtcNow().AddHours(2))];
		return harness;
	}

	[Fact]
	public async Task The_login_expiry_is_announced_once_n_days_ahead()
	{
		var harness = Claude();

		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.Time.SetUtcNow(Start.AddDays(3).AddMinutes(-1));
		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.Sender.Sent.ShouldNotContain(message => message.Title == Expiring);

		harness.Time.SetUtcNow(Start.AddDays(3));
		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.Time.Advance(TimeSpan.FromHours(1));
		await harness.Monitor.Poll(Provider.Claude, Token);

		var alert = harness.Sender.Sent.Where(message => message.Title == Expiring).ShouldHaveSingleItem();
		alert.Body.ShouldContain("claude auth login");
		alert.Body.ShouldContain("2026-09-24 12:00 UTC");
		harness.States.Stored[Provider.Claude].CredentialExpiryAlertedFor.ShouldBe(Start.AddDays(10));
	}

	[Fact]
	public async Task A_new_login_is_announced_again_before_its_own_expiry()
	{
		var harness = Claude();
		harness.Time.SetUtcNow(Start.AddDays(4));
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Session.RefreshTokenExpiresAt = Start.AddDays(40);
		harness.Time.SetUtcNow(Start.AddDays(5));
		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.Time.SetUtcNow(Start.AddDays(34));
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Sender.Sent.Count(message => message.Title == Expiring).ShouldBe(2);
	}

	[Fact]
	public async Task The_alert_delay_follows_the_settings()
	{
		var harness = Claude();
		var settings = harness.SettingsRepository.Stored!;
		harness.SettingsRepository.Stored = settings with { Notifications = settings.Notifications with { CredentialExpiryAlertDays = 2 } };

		harness.Time.SetUtcNow(Start.AddDays(7));
		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.Sender.Sent.ShouldNotContain(message => message.Title == Expiring);
		harness.Time.SetUtcNow(Start.AddDays(8));
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Sender.Sent.ShouldContain(message => message.Title == Expiring);
	}

	[Fact]
	public async Task An_undelivered_early_warning_is_sent_again()
	{
		var harness = Claude();
		harness.Time.SetUtcNow(Start.AddDays(4));
		harness.Sender.Failure = new HttpRequestException("ntfy returned HTTP 502.");
		await harness.Monitor.Poll(Provider.Claude, Token);
		harness.States.Stored[Provider.Claude].CredentialExpiryAlertedFor.ShouldBeNull();

		harness.Sender.Failure = null;
		harness.Time.Advance(TimeSpan.FromMinutes(3));
		await harness.Monitor.Poll(Provider.Claude, Token);

		harness.Sender.Sent.ShouldContain(message => message.Title == Expiring);
	}

	[Fact]
	public async Task The_keep_alive_announces_the_expiry_too()
	{
		var harness = Claude();
		harness.Time.SetUtcNow(Start.AddDays(4));
		harness.Session.ExpiresAt = Start.AddDays(4).AddMinutes(2);

		await harness.KeepAlive.Run(Token);

		harness.Sender.Sent.ShouldContain(message => message.Title == Expiring);
	}

	[Fact]
	public async Task Persistent_rate_limiting_alerts_once_after_the_threshold_then_recovers()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => throw new ProviderException(ProviderErrorCodes.RateLimited, "HTTP 429");
		for (var i = 0; i < 4; i++)
		{
			await harness.Monitor.Poll(Provider.Codex, Token);
			harness.Time.SetUtcNow(harness.States.Stored[Provider.Codex].BackoffUntil!.Value);
		}

		harness.CodexReader.Respond = () => [Window("codex/primary", 10, harness.Time.GetUtcNow().AddHours(1))];
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.Sender.Sent.Select(message => message.Title).ShouldBe(["Codex : lectures en échec", "Codex : rétabli"]);
		harness.Sender.Sent[0].Body.ShouldStartWith("3 lectures limitées (429)");
		harness.States.Stored[Provider.Codex].ConsecutiveRateLimits.ShouldBe(0);
	}
}
