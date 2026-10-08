using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     <c>claude -p</c> with no tools, no session file and no project context. Never <c>--bare</c>: that mode ignores the OAuth
///     login and would not consume the subscription.
/// </summary>
internal sealed class ClaudePromptRunner(IOptions<ClaudeOptions> options, ILogger<ClaudePromptRunner> logger) : IPromptRunner
{
	public const string Prompt = "1+1=?";

	public Provider Provider => Provider.Claude;

	public async Task Run(string model, ReasoningEffort effort, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		string[] arguments =
		[
			"-p", Prompt,
			"--model", model,
			.. EffortArguments(effort),
			"--tools", "",
			"--output-format", "json",
			"--no-session-persistence",
			"--safe-mode",
			"--strict-mcp-config",
			"--permission-mode", "dontAsk",
			"--max-turns", "1"
		];

		var result = await CliProcess.Run(settings.Executable, arguments, settings.ResolveWorkingDirectory(), TimeSpan.FromSeconds(settings.PromptTimeoutSeconds), logger,
			cancellationToken);
		if (Classify(result) is not { } failure)
		{
			return;
		}

		if (failure.Code == ProviderErrorCode.CliUnsupportedOption)
		{
			var version = await CliProcess.ReadVersion(settings.Executable, settings.ResolveWorkingDirectory(), logger, cancellationToken);
			throw new ProviderException(failure.Code, $"{failure.Message} (claude {version}: update the options of ClaudePromptRunner)");
		}

		throw failure;
	}

	/// <summary>
	///     The CLI ignores <c>--effort none</c>: no reasoning is the lowest level with thinking disabled.
	/// </summary>
	private static string[] EffortArguments(ReasoningEffort effort)
	{
		return effort == ReasoningEffort.None
			? ["--effort", ReasoningEffort.Low.ToCliValue(), "--settings", """{"alwaysThinkingEnabled":false}"""]
			: ["--effort", effort.ToCliValue()];
	}

	/// <summary>
	///     Success is exit code 0 with <c>is_error: false</c>; failures are classified from <c>api_error_status</c> and the text.
	/// </summary>
	internal static ProviderException? Classify(CliResult result)
	{
		var (isError, status, text) = ReadOutput(result.StandardOutput);
		if (result.ExitCode == 0 && isError == false)
		{
			return null;
		}

		var message = string.IsNullOrWhiteSpace(text) ? result.StandardError.Trim() : text;
		if (message.Length > 500)
		{
			message = message[..500];
		}

		if (string.IsNullOrWhiteSpace(message))
		{
			message = $"claude exited with code {result.ExitCode}.";
		}

		var code = true switch
		{
			// No JSON result: the argument parser stopped the CLI before the run.
			_ when isError is null && CliProcess.IsUnsupportedOption(message) => ProviderErrorCode.CliUnsupportedOption,
			_ when status is 401 or 403 || Contains(message, "Login expired") || Contains(message, "Not logged in") || Contains(message, "OAuth token") => ProviderErrorCode.AuthExpired,
			_ when Contains(message, "hit your") && Contains(message, "limit") => ProviderErrorCode.UsageLimit,
			_ when status == 429 || Contains(message, "429") => ProviderErrorCode.RateLimited,
			_ when status == 529 || Contains(message, "529") || Contains(message, "Overloaded") => ProviderErrorCode.Overloaded,
			_ => ProviderErrorCode.TriggerFailed
		};
		return new(code, message);
	}

	private static (bool? IsError, int? Status, string Text) ReadOutput(string output)
	{
		try
		{
			using var document = JsonDocument.Parse(output);
			var root = document.RootElement;
			bool? isError = root.TryGetProperty("is_error", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean() : null;
			int? status = root.TryGetProperty("api_error_status", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : null;
			var text = root.TryGetProperty("result", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
			return (isError, status, text);
		}
		catch (JsonException)
		{
			// Not the JSON result (for instance an argument error printed before the run): the text is used as is.
			return (null, null, output.Trim());
		}
	}

	private static bool Contains(string text, string value)
	{
		return text.Contains(value, StringComparison.OrdinalIgnoreCase);
	}
}