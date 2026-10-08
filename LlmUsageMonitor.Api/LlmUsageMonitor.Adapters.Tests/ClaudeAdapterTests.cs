using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Adapters.Claude;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Adapters.Tests.FakeCli;

namespace LlmUsageMonitor.Adapters.Tests;

/// <summary>
///     The Claude session and prompt over a fake <c>claude</c> CLI, and the usage reader over a stubbed HTTP endpoint.
/// </summary>
public sealed class ClaudeAdapterTests
{
	private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddHours(8).TruncateToMilliseconds();
	private static readonly DateTimeOffset RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30).TruncateToMilliseconds();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_session_reads_the_expiries_of_the_cli_login()
	{
		using var cli = new FakeCli(Scenario());
		var credentials = WriteCredentials(cli, ExpiresAt);

		var token = await Session(cli, credentials).ReadToken(Token);

		token.ShouldBe(new ClaudeTokenInfo(ExpiresAt, RefreshTokenExpiresAt));
	}

	[Fact]
	public async Task Missing_or_empty_credentials_are_reported()
	{
		using var cli = new FakeCli(Scenario());
		var missing = Path.Combine(cli.Directory, "missing.json");
		var empty = Path.Combine(cli.Directory, "empty.json");
		await File.WriteAllTextAsync(empty, """{"claudeAiOauth":{}}""", Token);

		(await Should.ThrowAsync<ProviderException>(() => Session(cli, missing).ReadToken(Token))).Code.ShouldBe(ProviderErrorCode.CredentialsUnavailable);
		(await Should.ThrowAsync<ProviderException>(() => Session(cli, empty).ReadToken(Token))).Code.ShouldBe(ProviderErrorCode.AuthRequired);
	}

	[Fact]
	public async Task The_refresh_runs_claude_mcp_list_which_rewrites_the_login()
	{
		var refreshed = ExpiresAt.AddHours(8);
		using var cli = new FakeCli(Scenario());
		var credentials = WriteCredentials(cli, ExpiresAt);
		await File.WriteAllTextAsync(Path.Combine(cli.Directory, "fake-cli.json"), Scenario(new()
		{
			["mcp"] = new JsonObject { ["writeFile"] = new JsonObject { ["path"] = credentials, ["content"] = Credentials(refreshed) } }
		}).ToJsonString(), Token);
		var session = Session(cli, credentials);

		await session.RefreshThroughCli(Token);

		cli.Invocations.ShouldBe(["mcp list"]);
		(await session.ReadToken(Token)).ExpiresAt.ShouldBe(refreshed);
	}

	[Fact]
	public async Task A_refresh_command_the_cli_no_longer_knows_is_reported_with_its_version()
	{
		using var cli = new FakeCli(Scenario(new()
		{
			["mcp"] = Command(stderr: "error: unknown option '--strict'", exitCode: 1),
			["--version"] = Command("9.9.9 (Claude Code)")
		}));

		var exception = await Should.ThrowAsync<ProviderException>(() => Session(cli, WriteCredentials(cli, ExpiresAt)).RefreshThroughCli(Token));

		exception.Code.ShouldBe(ProviderErrorCode.CliUnsupportedOption);
		exception.Message.ShouldContain("9.9.9 (Claude Code)");
	}

	[Fact]
	public async Task A_failing_mcp_server_does_not_fail_the_refresh()
	{
		using var cli = new FakeCli(Scenario(new() { ["mcp"] = Command(stderr: "Failed to connect to the server", exitCode: 1) }));

		await Should.NotThrowAsync(() => Session(cli, WriteCredentials(cli, ExpiresAt)).RefreshThroughCli(Token));
	}

	[Fact]
	public async Task The_prompt_runs_once_without_tools_nor_session_on_the_requested_model()
	{
		using var cli = new FakeCli(Scenario(new() { ["-p"] = Command("""{"type":"result","is_error":false,"result":"2"}""") }));

		await Runner(cli).Run("claude-test", ReasoningEffort.High, Token);

		var invocation = cli.Invocations.ShouldHaveSingleItem();
		invocation.ShouldStartWith($"-p {ClaudePromptRunner.Prompt}");
		invocation.ShouldContain("--model claude-test");
		invocation.ShouldContain("--effort high");
		invocation.ShouldNotContain("--settings");
		invocation.ShouldContain("--no-session-persistence");
		invocation.ShouldContain("--max-turns 1");
		invocation.ShouldNotContain("--bare");
	}

	[Fact]
	public async Task No_reasoning_runs_at_the_lowest_effort_with_thinking_disabled()
	{
		using var cli = new FakeCli(Scenario(new() { ["-p"] = Command("""{"type":"result","is_error":false,"result":"2"}""") }));

		await Runner(cli).Run("claude-test", ReasoningEffort.None, Token);

		var invocation = cli.Invocations.ShouldHaveSingleItem();
		invocation.ShouldContain("--effort low");
		invocation.ShouldContain("""--settings {"alwaysThinkingEnabled":false}""");
	}

	[Fact]
	public async Task A_prompt_refused_by_the_usage_limit_is_classified()
	{
		using var cli = new FakeCli(Scenario(new() { ["-p"] = Command("""{"type":"result","is_error":true,"result":"You've hit your limit · resets 5pm"}""", exitCode: 1) }));

		var exception = await Should.ThrowAsync<ProviderException>(() => Runner(cli).Run("claude-test", ReasoningEffort.Low, Token));

		exception.Code.ShouldBe(ProviderErrorCode.UsageLimit);
	}

	[Fact]
	public async Task The_reader_sends_the_cli_token_and_parses_the_usage()
	{
		using var cli = new FakeCli(Scenario());
		var handler = new StubHandler(HttpStatusCode.OK, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-usage.json"), Token));

		var windows = await Reader(WriteCredentials(cli, ExpiresAt), handler).Read(Token);

		windows.ShouldBe(ClaudeUsageParser.Parse(Fixtures.Load("claude-usage.json")));
		var request = handler.Requests.ShouldHaveSingleItem();
		request.RequestUri!.ToString().ShouldBe("https://api.anthropic.com/api/oauth/usage?cedar_ember=1&skip_spend=1");
		request.Headers.Authorization!.ToString().ShouldBe("Bearer access-token");
		request.Headers.GetValues("anthropic-beta").ShouldBe(["oauth-2025-04-20"]);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, ProviderErrorCode.AuthExpired)]
	[InlineData(HttpStatusCode.Forbidden, ProviderErrorCode.AccessDenied)]
	[InlineData(HttpStatusCode.TooManyRequests, ProviderErrorCode.RateLimited)]
	[InlineData(HttpStatusCode.NotFound, ProviderErrorCode.HttpError)]
	public async Task An_http_error_is_mapped_to_its_code_without_retrying(HttpStatusCode status, ProviderErrorCode code)
	{
		using var cli = new FakeCli(Scenario());
		var handler = new StubHandler(status, "{}");

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(WriteCredentials(cli, ExpiresAt), handler).Read(Token));

		exception.Code.ShouldBe(code);
		// A 429 goes to the reading backoff: retrying would only extend the rate limiting.
		handler.Requests.Count.ShouldBe(1);
	}

	[Fact]
	public async Task An_invalid_document_is_an_invalid_response()
	{
		using var cli = new FakeCli(Scenario());

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(WriteCredentials(cli, ExpiresAt), new StubHandler(HttpStatusCode.OK, "<html>")).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.InvalidResponse);
	}

	[Fact]
	public async Task An_expired_login_is_reported_without_calling_the_endpoint()
	{
		using var cli = new FakeCli(Scenario());
		var handler = new StubHandler(HttpStatusCode.OK, "{}");

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(WriteCredentials(cli, DateTimeOffset.UtcNow.AddMinutes(-1)), handler).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.AuthExpired);
		handler.Requests.ShouldBeEmpty();
	}

	private static ClaudeSession Session(FakeCli cli, string credentialsPath)
	{
		return new(Options.Create(OptionsFor(cli, credentialsPath)), NullLogger<ClaudeSession>.Instance);
	}

	private static ClaudePromptRunner Runner(FakeCli cli)
	{
		return new(Options.Create(OptionsFor(cli, null)), NullLogger<ClaudePromptRunner>.Instance);
	}

	/// <summary>The reader as the module registers it (resilience included), over a stubbed endpoint.</summary>
	private static IUsageReader Reader(string credentialsPath, StubHandler handler)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Claude:CredentialsPath"] = credentialsPath }).Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(TimeProvider.System);
		new ClaudeAdapterModule().Load(services, configuration);
		services.AddHttpClient(ClaudeAdapterModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
		return services.BuildServiceProvider().GetServices<IUsageReader>().Single();
	}

	private static ClaudeOptions OptionsFor(FakeCli cli, string? credentialsPath)
	{
		return new() { Executable = Executable, WorkingDirectory = cli.Directory, CredentialsPath = credentialsPath };
	}

	private static string WriteCredentials(FakeCli cli, DateTimeOffset expiresAt)
	{
		var path = Path.Combine(cli.Directory, ".credentials.json");
		File.WriteAllText(path, Credentials(expiresAt));
		return path;
	}

	private static string Credentials(DateTimeOffset expiresAt)
	{
		return new JsonObject
		{
			["claudeAiOauth"] = new JsonObject
			{
				["accessToken"] = "access-token",
				["refreshToken"] = "refresh-token",
				["expiresAt"] = expiresAt.ToUnixTimeMilliseconds(),
				["refreshTokenExpiresAt"] = RefreshTokenExpiresAt.ToUnixTimeMilliseconds()
			}
		}.ToJsonString();
	}
}

/// <summary>Answers every request with the same status and body, and keeps the requests.</summary>
internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
	public List<HttpRequestMessage> Requests { get; } = [];

	public List<string> Bodies { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		lock (Requests)
		{
			Requests.Add(request);
		}

		if (request.Content is { } content)
		{
			Bodies.Add(await content.ReadAsStringAsync(cancellationToken));
		}

		return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}
}

internal static class DateTimeOffsetExtensions
{
	public static DateTimeOffset TruncateToMilliseconds(this DateTimeOffset value)
	{
		return DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());
	}
}
