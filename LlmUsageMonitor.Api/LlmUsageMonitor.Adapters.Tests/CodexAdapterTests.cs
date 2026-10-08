using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Adapters.Codex;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Adapters.Tests.FakeCli;

namespace LlmUsageMonitor.Adapters.Tests;

/// <summary>
///     The Codex reader and prompt over a fake <c>codex app-server</c> speaking JSON-RPC on stdio.
/// </summary>
public sealed class CodexAdapterTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_reader_asks_the_rate_limits_and_nothing_else()
	{
		using var cli = new FakeCli(Scenario(rpc: new() { ["account/rateLimits/read"] = new JsonObject { ["result"] = Fixture("codex-rate-limits.json") } }));

		var windows = await Reader(cli).Read(Token);

		windows.ShouldBe(CodexUsageParser.Parse(Fixtures.Load("codex-rate-limits.json")));
		cli.Invocations.ShouldBe(["app-server --listen stdio://"]);
		Methods(cli).ShouldBe(["initialize", "initialized", "account/rateLimits/read"]);
	}

	[Fact]
	public async Task A_signed_out_cli_is_reported_from_its_account_state()
	{
		using var cli = new FakeCli(Scenario(rpc: new()
		{
			["account/rateLimits/read"] = new JsonObject { ["error"] = Fixture("codex-rate-limits-signed-out.json") },
			["account/read"] = new JsonObject { ["result"] = Fixture("codex-account-signed-out.json") }
		}));

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(cli).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.AuthExpired);
		Methods(cli).ShouldContain("account/read");
	}

	[Fact]
	public async Task A_server_that_never_answers_is_a_timeout()
	{
		using var cli = new FakeCli(Scenario(rpc: new() { ["account/rateLimits/read"] = new JsonObject { ["silent"] = true } }));
		var watch = Stopwatch.StartNew();

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(cli, readTimeoutSeconds: 1).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.Timeout);
		watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public async Task An_argument_the_cli_no_longer_accepts_is_reported_with_its_version()
	{
		using var cli = new FakeCli(Scenario(new()
		{
			["app-server"] = Command(stderr: "error: unexpected argument '--listen' found", exitCode: 2),
			["--version"] = Command("codex-cli 9.9.9")
		}));

		var exception = await Should.ThrowAsync<ProviderException>(() => Reader(cli).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.CliUnsupportedOption);
		exception.Message.ShouldContain("codex-cli 9.9.9");
	}

	[Fact]
	public async Task A_missing_executable_is_reported_as_unavailable()
	{
		using var cli = new FakeCli(Scenario());
		var options = new CodexOptions { Executable = Path.Combine(cli.Directory, "missing-codex"), WorkingDirectory = cli.Directory };

		var exception = await Should.ThrowAsync<ProviderException>(() => new CodexUsageReader(Options.Create(options), NullLogger<CodexUsageReader>.Instance).Read(Token));

		exception.Code.ShouldBe(ProviderErrorCode.CliUnavailable);
		exception.Message.ShouldContain("Codex:Executable");
	}

	[Fact]
	public async Task The_prompt_runs_one_ephemeral_read_only_turn_until_it_completes()
	{
		using var cli = new FakeCli(Scenario(rpc: PromptRpc(new JsonObject { ["status"] = "completed" })));

		await Runner(cli).Run("gpt-test", ReasoningEffort.None, Token);

		Methods(cli).ShouldBe(["initialize", "initialized", "thread/start", "turn/start"]);
		var thread = cli.Messages.Single(message => message.GetProperty("method").GetString() == "thread/start").GetProperty("params");
		thread.GetProperty("ephemeral").GetBoolean().ShouldBeTrue();
		thread.GetProperty("sandbox").GetString().ShouldBe("read-only");
		thread.GetProperty("approvalPolicy").GetString().ShouldBe("never");
		thread.GetProperty("model").GetString().ShouldBe("gpt-test");
		var turn = cli.Messages.Single(message => message.GetProperty("method").GetString() == "turn/start").GetProperty("params");
		turn.GetProperty("threadId").GetString().ShouldBe("thread-1");
		turn.GetProperty("input")[0].GetProperty("text").GetString().ShouldBe(CodexPromptRunner.Prompt);
		turn.GetProperty("effort").GetString().ShouldBe("none");
	}

	[Fact]
	public async Task A_failed_turn_is_mapped_from_its_error_info()
	{
		var failed = new JsonObject { ["status"] = "failed", ["error"] = new JsonObject { ["message"] = "You've hit your usage limit.", ["codexErrorInfo"] = "usageLimitExceeded" } };
		using var cli = new FakeCli(Scenario(rpc: PromptRpc(failed)));

		var exception = await Should.ThrowAsync<ProviderException>(() => Runner(cli).Run("gpt-test", ReasoningEffort.Low, Token));

		exception.Code.ShouldBe(ProviderErrorCode.UsageLimit);
		exception.Message.ShouldBe("You've hit your usage limit.");
	}

	[Fact]
	public async Task A_cancelled_prompt_is_a_cancellation_not_a_provider_failure()
	{
		using var cli = new FakeCli(Scenario(rpc: new() { ["thread/start"] = new JsonObject { ["silent"] = true } }));
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

		var exception = await Record.ExceptionAsync(() => Runner(cli).Run("gpt-test", ReasoningEffort.Low, cancellation.Token));

		exception.ShouldBeAssignableTo<OperationCanceledException>();
	}

	private static CodexUsageReader Reader(FakeCli cli, int readTimeoutSeconds = 20)
	{
		var options = new CodexOptions { Executable = Executable, WorkingDirectory = cli.Directory, ReadTimeoutSeconds = readTimeoutSeconds };
		return new(Options.Create(options), NullLogger<CodexUsageReader>.Instance);
	}

	private static CodexPromptRunner Runner(FakeCli cli)
	{
		var options = new CodexOptions { Executable = Executable, WorkingDirectory = cli.Directory, PromptTimeoutSeconds = 20 };
		return new(Options.Create(options), NullLogger<CodexPromptRunner>.Instance);
	}

	/// <summary>A thread, then a turn that ends with the given turn, announced by a notification of another thread first.</summary>
	private static JsonObject PromptRpc(JsonObject turn)
	{
		return new()
		{
			["thread/start"] = new JsonObject { ["result"] = new JsonObject { ["thread"] = new JsonObject { ["id"] = "thread-1" } } },
			["turn/start"] = new JsonObject
			{
				["result"] = new JsonObject(),
				["notifications"] = new JsonArray(
					new JsonObject { ["method"] = "turn/completed", ["params"] = new JsonObject { ["threadId"] = "other", ["turn"] = new JsonObject { ["status"] = "failed" } } },
					new JsonObject { ["method"] = "turn/completed", ["params"] = new JsonObject { ["threadId"] = "thread-1", ["turn"] = turn } })
			}
		};
	}

	private static List<string?> Methods(FakeCli cli)
	{
		return cli.Messages.Select(message => message.GetProperty("method").GetString()).ToList();
	}

	private static JsonNode Fixture(string name)
	{
		return JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))!;
	}
}
