using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>Earned resets over app-server, with an explicit credit and a persisted idempotency key.</summary>
internal sealed class CodexResetCreditConsumer(IOptions<CodexOptions> options, ILogger<CodexResetCreditConsumer> logger) : IResetCreditConsumer
{
	public Provider Provider => Provider.Codex;
	public async Task<ResetCreditResult> Consume(string creditId, string idempotencyKey, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ReadTimeoutSeconds));
		try
		{
			await using var server = await CodexAppServer.Start(options.Value, "account/rateLimitResetCredit/consume", logger, timeout.Token);
			var result = await server.Request("account/rateLimitResetCredit/consume", new { creditId, idempotencyKey }, timeout.Token);
			var outcome = result.TryGetProperty("outcome", out var value) ? value.GetString() : null;
			return outcome switch
			{
				"reset" or "alreadyRedeemed" => new(true, outcome),
				"nothingToReset" or "noCredit" => new(false, outcome),
				_ => throw new ProviderException(ProviderErrorCode.InvalidResponse, "Codex returned an unknown reset outcome; retry with the same idempotency key.")
			};
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCode.Timeout, "Codex reset request timed out.");
		}
		catch (CodexRpcException exception)
		{
			throw new ProviderException(CodexErrors.FromRpcError(exception) ?? ProviderErrorCode.FetchFailed, "Codex rejected the reset request.", exception);
		}
	}
}
