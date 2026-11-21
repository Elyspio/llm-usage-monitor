using System.Net;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Adapters.Copilot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class CopilotUsageParserTests
{
	[Fact]
	public void The_captured_response_gives_the_monthly_premium_requests_only()
	{
		var usage = CopilotUsageParser.Parse(Fixtures.Load("copilot-user.json"));

		var window = usage.Windows.ShouldHaveSingleItem();
		window.Id.ShouldBe(CopilotUsageParser.PremiumWindowId);
		window.UsedPercent.ShouldBe(45.9, 0.0001);
		window.WindowDurationMinutes.ShouldBeNull();
		window.ResetsAt.ShouldBe(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
		usage.ResetCredits.ShouldBeNull();
		usage.Quota.ShouldBe(new MonthlyQuota("business", 20000, 10835, 0, false, false, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
	}

	[Fact]
	public void An_unlimited_plan_is_never_used()
	{
		var usage = CopilotUsageParser.Parse(Json("""{ "quota_snapshots": { "premium_interactions": { "unlimited": true, "percent_remaining": 0 } } }"""));

		usage.Windows[0].UsedPercent.ShouldBe(0);
		usage.Quota!.Unlimited.ShouldBeTrue();
		usage.Quota.ResetsAt.ShouldBeNull();
	}

	[Fact]
	public void Without_a_percentage_the_share_follows_the_counts()
	{
		var usage = CopilotUsageParser.Parse(Json("""{ "quota_reset_date": "2026-12-01", "quota_snapshots": { "premium_interactions": { "entitlement": 300, "remaining": 75 } } }"""));

		usage.Windows[0].UsedPercent.ShouldBe(75);
		usage.Windows[0].ResetsAt.ShouldBe(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
	}

	[Theory]
	[InlineData("""{ "copilot_plan": "free" }""")]
	[InlineData("""{ "quota_snapshots": { "chat": { "percent_remaining": 100 } } }""")]
	public void An_account_without_premium_quota_has_no_usage_data(string json)
	{
		Should.Throw<ProviderException>(() => CopilotUsageParser.Parse(Json(json))).Code.ShouldBe(ProviderErrorCode.NoUsageData);
	}

	[Fact]
	public void A_premium_quota_without_any_count_is_rejected()
	{
		Should.Throw<ProviderException>(() => CopilotUsageParser.Parse(Json("""{ "quota_snapshots": { "premium_interactions": {} } }""")))
			.Code.ShouldBe(ProviderErrorCode.InvalidResponse);
	}

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}

public sealed class CopilotAdapterTests : IDisposable
{
	private const string ConfigWithLogin = """
	                                       // User settings belong in settings.json.
	                                       // This file is managed automatically.
	                                       {
	                                         "authTokens": { "https://github.com:octocat:github": { "token": "github-token" }, "https://github.com:octocat": { "token": "stored-token" }, "https://github.com:other": { "token": "other-token" } },
	                                         "loggedInUsers": [{ "host": "https://github.com", "login": "octocat" }],
	                                         "lastLoggedInUser": { "host": "https://github.com", "login": "octocat" }
	                                       }
	                                       """;

	private readonly string _directory = Directory.CreateTempSubdirectory("copilot-tests").FullName;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose() => Directory.Delete(_directory, true);

	[Fact]
	public void The_stored_token_is_the_one_of_the_last_signed_in_user()
	{
		CopilotCredentials.Parse(ConfigWithLogin).ShouldBe("stored-token");
		CopilotCredentials.Parse("""{ "loggedInUsers": [] }""").ShouldBeNull();
		CopilotCredentials.Parse("""{ "authTokens": { "https://github.com:octocat": "bare-token" }, "lastLoggedInUser": { "login": "octocat" } }""").ShouldBe("bare-token");
	}

	[Fact]
	public async Task The_reader_sends_the_stored_token_and_returns_the_quota()
	{
		await File.WriteAllTextAsync(Path.Combine(_directory, "config.json"), ConfigWithLogin, Token);
		var handler = new StubHandler(HttpStatusCode.OK, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "copilot-user.json"), Token));

		var usage = (await Reader(handler).ReadAccount(Token)).ShouldSucceed();

		usage.Quota!.Remaining.ShouldBe(10835);
		var request = handler.Requests.ShouldHaveSingleItem();
		request.RequestUri!.ToString().ShouldBe("https://api.github.com/copilot_internal/user");
		request.Headers.Authorization!.ToString().ShouldBe("token stored-token");
		request.Headers.UserAgent.ToString().ShouldNotBeEmpty();
	}

	[Fact]
	public async Task An_environment_token_wins_over_the_stored_login()
	{
		await File.WriteAllTextAsync(Path.Combine(_directory, "config.json"), ConfigWithLogin, Token);
		var handler = new StubHandler(HttpStatusCode.OK, """{ "quota_snapshots": { "premium_interactions": { "percent_remaining": 10 } } }""");

		(await Reader(handler, name => name == "GH_TOKEN" ? "env-token" : null).ReadAccount(Token)).ShouldSucceed();

		handler.Requests.ShouldHaveSingleItem().Headers.Authorization!.Parameter.ShouldBe("env-token");
	}

	[Fact]
	public async Task A_missing_login_asks_to_sign_in_without_calling_github()
	{
		var handler = new StubHandler(HttpStatusCode.OK, "{}");

		var error = (await Reader(handler).ReadAccount(Token)).ShouldFail().ShouldBeOfType<ProviderError>();

		error.ProviderCode.ShouldBe(ProviderErrorCode.CredentialsUnavailable);
		handler.Requests.ShouldBeEmpty();
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, ProviderErrorCode.AuthExpired)]
	[InlineData(HttpStatusCode.Forbidden, ProviderErrorCode.AccessDenied)]
	[InlineData(HttpStatusCode.TooManyRequests, ProviderErrorCode.RateLimited)]
	public async Task Http_failures_map_to_provider_errors(HttpStatusCode status, ProviderErrorCode expected)
	{
		await File.WriteAllTextAsync(Path.Combine(_directory, "config.json"), ConfigWithLogin, Token);

		var error = (await Reader(new StubHandler(status, "{}")).ReadAccount(Token)).ShouldFail().ShouldBeOfType<ProviderError>();

		error.ProviderCode.ShouldBe(expected);
	}

	/// <summary>The reader as the module registers it (resilience included), over a stubbed endpoint and a controlled environment.</summary>
	private IUsageReader Reader(StubHandler handler, Func<string, string?>? environment = null)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Copilot:ConfigDirectory"] = _directory }).Build();
		var services = new ServiceCollection();
		services.AddLogging();
		new CopilotAdapterModule().Load(services, configuration);
		services.AddHttpClient(CopilotAdapterModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
		var provider = services.BuildServiceProvider();
		provider.GetServices<IUsageReader>().ShouldHaveSingleItem().Provider.ShouldBe(Provider.Copilot);
		return new CopilotUsageReader(provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<IOptions<CopilotOptions>>())
		{
			Environment = environment ?? (_ => null)
		};
	}
}
