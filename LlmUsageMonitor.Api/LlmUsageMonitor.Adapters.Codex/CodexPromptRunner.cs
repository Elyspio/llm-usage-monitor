using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Sends the minimal prompt in an ephemeral, read-only thread and waits for <c>turn/completed</c>.
/// </summary>
internal sealed class CodexPromptRunner(IOptions<CodexOptions> options) : IPromptRunner
{
	public const string Prompt = "1+1=?";

	public Provider Provider => Provider.Codex;

	public async Task Run(string model, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(settings.PromptTimeoutSeconds));
		try
		{
			await using var server = await CodexAppServer.Start(settings, timeout.Token);

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
				effort = settings.Effort
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
						: new(ProviderErrorCodes.TriggerFailed, $"Codex turn ended with status {turn.GetProperty("status").GetString()}.");
				}
			}

			throw new ProviderException(ProviderErrorCodes.CliExited, "Codex exited before completing the turn.");
		}
		catch (CodexRpcException exception)
		{
			throw new ProviderException(ProviderErrorCodes.TriggerFailed, exception.Message ?? "Codex rejected the prompt.", exception);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCodes.Timeout, "Codex prompt timed out.");
		}
	}

	private static bool IsForThread(JsonElement parameters, string? threadId)
	{
		return parameters.ValueKind == JsonValueKind.Object
		       && parameters.TryGetProperty("threadId", out var id)
		       && id.GetString() == threadId;
	}

	/// <summary>
	///     Maps a <c>TurnError</c>: <c>codexErrorInfo</c> is either a string or an object keyed by the error kind.
	/// </summary>
	internal static ProviderException MapTurnError(JsonElement error)
	{
		var message = error.TryGetProperty("message", out var text) ? text.GetString() ?? "Codex turn failed." : "Codex turn failed.";
		var kind = error.TryGetProperty("codexErrorInfo", out var info)
			? info.ValueKind switch
			{
				JsonValueKind.String => info.GetString(),
				JsonValueKind.Object => info.EnumerateObject().Select(property => property.Name).FirstOrDefault(),
				_ => null
			}
			: null;

		var code = kind switch
		{
			"unauthorized" => ProviderErrorCodes.AuthExpired,
			"usageLimitExceeded" => ProviderErrorCodes.UsageLimit,
			"rateLimitExceeded" => ProviderErrorCodes.RateLimited,
			_ => ProviderErrorCodes.TriggerFailed
		};
		return new(code, message);
	}
}