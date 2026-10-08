using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Sends the minimal prompt in an ephemeral, read-only thread and waits for <c>turn/completed</c>.
/// </summary>
internal sealed class CodexPromptRunner(IOptions<CodexOptions> options, ILogger<CodexPromptRunner> logger) : IPromptRunner
{
	public const string Prompt = "1+1=?";

	public Provider Provider => Provider.Codex;

	public async Task Run(string model, ReasoningEffort effort, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(settings.PromptTimeoutSeconds));
		try
		{
			await using var server = await CodexAppServer.Start(settings, "prompt", logger, timeout.Token);

			var thread = await server.Request("thread/start", new
			{
				ephemeral = true,
				cwd = settings.ResolveWorkingDirectory(),
				model,
				sandbox = "read-only",
				approvalPolicy = "never"
			}, timeout.Token);
			var threadId = thread.GetProperty("thread").GetProperty("id").GetString();

			await server.Request("turn/start", new
			{
				threadId,
				input = new object[] { new { type = "text", text = Prompt, text_elements = Array.Empty<object>() } },
				// "none" is accepted by the models although their advertised levels start at low.
				effort = effort.ToCliValue()
			}, timeout.Token);

			await foreach (var notification in server.Notifications.ReadAllAsync(timeout.Token))
			{
				if (!IsForThread(notification.Params, threadId))
				{
					continue;
				}

				if (notification.Method == "error"
				    && notification.Params.TryGetProperty("willRetry", out var willRetry) && willRetry.ValueKind == JsonValueKind.False
				    && notification.Params.TryGetProperty("error", out var error))
				{
					throw MapTurnError(error);
				}

				if (notification.Method == "turn/completed")
				{
					var turn = notification.Params.GetProperty("turn");
					if (turn.GetProperty("status").GetString() == "completed")
					{
						return;
					}

					throw turn.TryGetProperty("error", out var turnError) && turnError.ValueKind == JsonValueKind.Object
						? MapTurnError(turnError)
						: new(ProviderErrorCode.TriggerFailed, $"Codex turn ended with status {turn.GetProperty("status").GetString()}.");
				}
			}

			throw new ProviderException(ProviderErrorCode.CliExited, "Codex exited before completing the turn.");
		}
		catch (CodexRpcException exception)
		{
			var code = CodexErrors.FromRpcError(exception) ?? ProviderErrorCode.TriggerFailed;
			var message = exception.Message;
			if (code == ProviderErrorCode.CliUnsupportedOption)
			{
				var version = await CliProcess.ReadVersion(settings.Executable, settings.ResolveWorkingDirectory(), logger, cancellationToken);
				message = $"{message} (codex {version}: update the requests of CodexPromptRunner)";
			}

			throw new ProviderException(code, message, exception);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCode.Timeout, "Codex prompt timed out.");
		}
	}

	private static bool IsForThread(JsonElement parameters, string? threadId)
	{
		return parameters.ValueKind == JsonValueKind.Object
		       && parameters.TryGetProperty("threadId", out var id)
		       && id.GetString() == threadId;
	}

	/// <summary>
	///     Maps a <c>TurnError</c> from its <c>codexErrorInfo</c>.
	/// </summary>
	internal static ProviderException MapTurnError(JsonElement error)
	{
		var message = error.TryGetProperty("message", out var text) ? text.GetString() ?? "Codex turn failed." : "Codex turn failed.";
		var code = error.TryGetProperty("codexErrorInfo", out var info) ? CodexErrors.FromErrorInfo(info) : null;
		return new(code ?? ProviderErrorCode.TriggerFailed, message);
	}
}