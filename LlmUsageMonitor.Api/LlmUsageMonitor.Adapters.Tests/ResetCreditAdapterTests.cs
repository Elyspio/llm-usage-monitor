using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Adapters.Claude;
using LlmUsageMonitor.Adapters.Codex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Adapters.Tests.FakeCli;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class ResetCreditAdapterTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;
	[Fact]
	public void Codex_preserves_the_authoritative_count_and_null_details()
	{
		using var document = JsonDocument.Parse("""{"rateLimitResetCredits":{"availableCount":5,"credits":null}}""");
		var balance = CodexUsageParser.ParseCredits(document.RootElement)!;
		balance.AvailableCount.ShouldBe(5);
		balance.Credits.ShouldBeNull();
		var captured = CodexUsageParser.ParseCredits(Fixtures.Load("codex-rate-limits.json"))!;
		captured.Credits!.Count.ShouldBe(2);
		captured.Credits[0].IsUsable.ShouldBeTrue();
		captured.Credits[0].ExpiresAt.ShouldNotBeNull();
	}

	[Theory]
	[InlineData("reset", true)]
	[InlineData("alreadyRedeemed", true)]
	[InlineData("nothingToReset", false)]
	[InlineData("noCredit", false)]
	public async Task Codex_consumes_only_the_selected_credit_with_the_given_key(string outcome, bool succeeded)
	{
		using var cli = new FakeCli(Scenario(rpc: new() { ["account/rateLimitResetCredit/consume"] = new JsonObject { ["result"] = new JsonObject { ["outcome"] = outcome } } }));
		var consumer = new CodexResetCreditConsumer(Options.Create(new CodexOptions { Executable = Executable, WorkingDirectory = cli.Directory }), NullLogger<CodexResetCreditConsumer>.Instance);
		var key = Guid.NewGuid().ToString();
		(await consumer.Consume("selected-credit", key, Token)).Succeeded.ShouldBe(succeeded);
		cli.Messages.Select(message => message.GetProperty("method").GetString()).ShouldBe(["initialize", "initialized", "account/rateLimitResetCredit/consume"]);
		var parameters = cli.Messages.Last().GetProperty("params");
		parameters.GetProperty("creditId").GetString().ShouldBe("selected-credit");
		parameters.GetProperty("idempotencyKey").GetString().ShouldBe(key);
	}

	[Fact]
	public void Claude_preserves_grant_counts_expiration_eligibility_and_affected_windows()
	{
		using var document = JsonDocument.Parse("""
		{"cedar_ember":{"eligible":true,"grants":[{"id":"grant_1","label":"Weekly reset","resets_left":3,"starts_at":null,"ends_at":"2026-10-05T00:00:00Z","clears":["seven_day"],"paused":false,"usable_now":true,"use_requires_limit":false}]}}
		""");
		var balance = ClaudeResetCreditParser.Parse(document.RootElement)!;
		balance.AvailableCount.ShouldBe(3);
		var credit = balance.Credits!.ShouldHaveSingleItem();
		credit.WindowIds.ShouldBe(["seven_day"]);
		credit.RequiresLimit.ShouldBeFalse();
		credit.IsUsable.ShouldBeTrue();
		credit.ExpiresAt.ShouldBe(DateTimeOffset.Parse("2026-10-05T00:00:00Z"));
		ClaudeResetCreditParser.Parse(Fixtures.Load("claude-usage.json")).ShouldBeNull();
	}

	[Theory]
	[InlineData("reset", true, false)]
	[InlineData("already_used", true, false)]
	[InlineData("not_limited", false, false)]
	[InlineData("cooldown", false, true)]
	[InlineData("unavailable", false, true)]
	public async Task Claude_redemption_uses_the_cli_organization_and_durable_request_id(string outcome, bool succeeded, bool retryable)
	{
		using var cli = new FakeCli(Scenario());
		var credentials = Path.Combine(cli.Directory, ".credentials.json");
		var config = Path.Combine(cli.Directory, ".claude.json");
		const string organization = "7d259c85-9958-4871-9cdc-f4a3d4e7f133";
		await File.WriteAllTextAsync(credentials, """{"claudeAiOauth":{"accessToken":"test-token"}}""", Token);
		await File.WriteAllTextAsync(config, """{"oauthAccount":{"organizationUuid":"7d259c85-9958-4871-9cdc-f4a3d4e7f133"}}""", Token);
		var handler = new StubHandler(HttpStatusCode.OK, new JsonObject { ["result"] = outcome }.ToJsonString());
		var services = new ServiceCollection();
		services.AddLogging();
		new ClaudeAdapterModule().Load(services, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{ ["Claude:CredentialsPath"] = credentials, ["Claude:AccountConfigPath"] = config }).Build());
		services.AddHttpClient(ClaudeAdapterModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
		await using var provider = services.BuildServiceProvider();
		var key = Guid.NewGuid().ToString();
		var result = await provider.GetRequiredService<IResetCreditConsumer>().Consume("grant_1", key, Token);
		result.Succeeded.ShouldBe(succeeded);
		result.Retryable.ShouldBe(retryable);
		var request = handler.Requests.ShouldHaveSingleItem();
		request.RequestUri!.AbsolutePath.ShouldBe($"/api/organizations/{organization}/reset_rate_limits");
		request.Headers.Authorization!.Parameter.ShouldBe("test-token");
		using var body = JsonDocument.Parse(handler.Bodies.ShouldHaveSingleItem());
		body.RootElement.GetProperty("program").GetString().ShouldBe("cedar_ember");
		body.RootElement.GetProperty("grant_id").GetString().ShouldBe("grant_1");
		body.RootElement.GetProperty("request_id").GetString().ShouldBe(key);
		cli.Invocations.ShouldBeEmpty();
	}
}
