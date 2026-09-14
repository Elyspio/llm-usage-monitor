using System.Text.Json;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Adapters.Claude;
using LlmUsageMonitor.Adapters.Codex;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class ClaudeUsageParserTests
{
	[Fact]
	public void The_captured_response_gives_the_session_and_weekly_windows_only()
	{
		var windows = ClaudeUsageParser.Parse(Fixtures.Load("claude-usage.json"));

		windows.Select(window => window.Id).ShouldBe(["five_hour", "seven_day"]);
		var session = windows[0];
		session.UsedPercent.ShouldBe(2);
		session.RemainingPercent.ShouldBe(98);
		session.WindowDurationMinutes.ShouldBe(300);
		session.ResetsAt.ShouldBe(new DateTimeOffset(2026, 9, 14, 6, 19, 59, 626, TimeSpan.Zero).AddTicks(2500));
		windows[1].WindowDurationMinutes.ShouldBe(10_080);
	}

	[Fact]
	public void Reset_times_may_be_unix_seconds_and_usage_above_100_leaves_nothing()
	{
		var windows = ClaudeUsageParser.Parse(Json("""{ "five_hour": { "utilization": 120, "resets_at": 1789806548 } }"""));

		windows[0].ResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1789806548));
		windows[0].RemainingPercent.ShouldBe(0);
	}

	[Theory]
	[InlineData("""{ "five_hour": { "utilization": -1, "resets_at": null } }""")]
	[InlineData("""{ "five_hour": { "utilization": "12", "resets_at": null } }""")]
	[InlineData("""{ "five_hour": { "utilization": 1, "resets_at": "tomorrow" } }""")]
	[InlineData("""[]""")]
	public void Invalid_values_are_rejected(string json)
	{
		Should.Throw<ProviderException>(() => ClaudeUsageParser.Parse(Json(json))).Code.ShouldBe(ProviderErrorCodes.InvalidResponse);
	}

	[Theory]
	[InlineData(0, """{"is_error":false,"result":"2"}""", null)]
	[InlineData(1, """{"is_error":true,"api_error_status":401,"result":"Invalid token"}""", ProviderErrorCodes.AuthExpired)]
	[InlineData(1, """{"is_error":true,"result":"Login expired · Please run /login"}""", ProviderErrorCodes.AuthExpired)]
	[InlineData(1, """{"is_error":true,"result":"You've hit your session limit"}""", ProviderErrorCodes.UsageLimit)]
	[InlineData(1, """{"is_error":true,"api_error_status":429,"result":"Request rejected (429)"}""", ProviderErrorCodes.RateLimited)]
	[InlineData(0, """{"subtype":"success","is_error":true,"result":"API Error: 529 Overloaded"}""", ProviderErrorCodes.TriggerFailed)]
	[InlineData(1, "error: unknown option '--safe-mode'", ProviderErrorCodes.TriggerFailed)]
	public void Prompt_results_are_classified_from_is_error_the_status_and_the_text(int exitCode, string output, string? expected)
	{
		ClaudePromptRunner.Classify(new CliResult(exitCode, output, ""))?.Code.ShouldBe(expected);
	}

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}

public sealed class CodexUsageParserTests
{
	[Fact]
	public void The_captured_response_gives_one_weekly_window_on_the_team_plan()
	{
		var window = CodexUsageParser.Parse(Fixtures.Load("codex-rate-limits.json")).ShouldHaveSingleItem();

		window.Id.ShouldBe("codex/primary");
		window.UsedPercent.ShouldBe(11);
		window.WindowDurationMinutes.ShouldBe(10_080);
		window.ResetsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1789806548));
	}

	[Fact]
	public void Older_clients_expose_only_the_legacy_snapshot()
	{
		var windows = CodexUsageParser.Parse(Json("""
			{ "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 30, "windowDurationMins": 300, "resetsAt": null },
			  "secondary": { "usedPercent": 5, "windowDurationMins": 10080, "resetsAt": 1789806548 } } }
			"""));

		windows.Select(window => window.Id).ShouldBe(["codex/primary", "codex/secondary"]);
		windows[0].ResetsAt.ShouldBeNull();
	}

	[Fact]
	public void No_bucket_means_no_window()
	{
		CodexUsageParser.Parse(Json("""{ "rateLimitsByLimitId": {}, "rateLimits": null }""")).ShouldBeEmpty();
	}

	[Fact]
	public void A_non_positive_duration_is_rejected()
	{
		var json = """{ "rateLimitsByLimitId": { "codex": { "primary": { "usedPercent": 1, "windowDurationMins": 0, "resetsAt": null } } } }""";

		Should.Throw<ProviderException>(() => CodexUsageParser.Parse(Json(json))).Code.ShouldBe(ProviderErrorCodes.InvalidResponse);
	}

	[Theory]
	[InlineData("""{ "message": "denied", "codexErrorInfo": "unauthorized" }""", ProviderErrorCodes.AuthExpired)]
	[InlineData("""{ "message": "limit", "codexErrorInfo": "usageLimitExceeded" }""", ProviderErrorCodes.UsageLimit)]
	[InlineData("""{ "message": "slow down", "codexErrorInfo": "rateLimitExceeded" }""", ProviderErrorCodes.RateLimited)]
	[InlineData("""{ "message": "stream", "codexErrorInfo": { "responseStreamDisconnected": { "httpStatusCode": 502 } } }""", ProviderErrorCodes.TriggerFailed)]
	[InlineData("""{ "message": "unknown", "codexErrorInfo": null }""", ProviderErrorCodes.TriggerFailed)]
	public void Turn_errors_are_mapped_from_codexErrorInfo(string error, string expected)
	{
		CodexPromptRunner.MapTurnError(Json(error)).Code.ShouldBe(expected);
	}

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}

internal static class Fixtures
{
	public static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))).RootElement;
}
