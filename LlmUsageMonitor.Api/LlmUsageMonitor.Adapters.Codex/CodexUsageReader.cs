using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Reads the Codex limits with <c>account/rateLimits/read</c>; never starts a thread or a model turn.
/// </summary>
internal sealed class CodexUsageReader(IOptions<CodexOptions> options, ILogger<CodexUsageReader> logger) : IUsageReader
{
	public Provider Provider => Provider.Codex;

	public async Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken)
	{
		var settings = options.Value;
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(settings.ReadTimeoutSeconds));
		try
		{
			await using var server = await CodexAppServer.Start(settings, "account/rateLimits/read", logger, timeout.Token);
			try
			{
				var result = await server.Request("account/rateLimits/read", new { }, timeout.Token);
				return CodexUsageParser.Parse(result);
			}
			catch (CodexRpcException exception)
			{
				// The error has no structure of its own when the login is gone: the account state tells.
				var code = CodexErrors.FromRpcError(exception) ?? await ReadAccountFailure(server, timeout.Token) ?? ProviderErrorCodes.FetchFailed;
				throw await Failure(code, exception, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCodes.Timeout, "Codex usage request timed out.");
		}
	}

	private static async Task<string?> ReadAccountFailure(CodexAppServer server, CancellationToken cancellationToken)
	{
		try
		{
			return CodexErrors.IsSignedOut(await server.Request("account/read", new { }, cancellationToken)) ? ProviderErrorCodes.AuthExpired : null;
		}
		catch (CodexRpcException)
		{
			return null;
		}
	}

	private async Task<ProviderException> Failure(string code, CodexRpcException exception, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		var message = code switch
		{
			ProviderErrorCodes.AuthExpired => "The Codex CLI is no longer signed in. Run `codex login --device-auth` on the service host.",
			ProviderErrorCodes.RateLimited => "Codex rate limits the account request.",
			ProviderErrorCodes.CliUnsupportedOption =>
				$"{exception.Message} (codex {await CliProcess.ReadVersion(settings.Executable, settings.ResolveWorkingDirectory(), logger, cancellationToken)}: update the requests of CodexUsageReader)",
			_ => $"Codex rejected the account request: {exception.Message}"
		};
		return new(code, message, exception);
	}
}

/// <summary>
///     Port of the TypeScript reader: one window per bucket and per <c>primary</c> / <c>secondary</c> slot.
/// </summary>
public static class CodexUsageParser
{
	public static IReadOnlyList<UsageWindow> Parse(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Object)
		{
			throw new ProviderException(ProviderErrorCodes.InvalidResponse, "Codex returned invalid protocol data.");
		}

		var buckets = new List<(string Id, JsonElement Snapshot)>();
		if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) && byLimitId.ValueKind == JsonValueKind.Object)
		{
			buckets.AddRange(byLimitId.EnumerateObject().Where(bucket => bucket.Value.ValueKind == JsonValueKind.Object).Select(bucket => (bucket.Name, bucket.Value)));
		}

		// Older CLI versions expose only the legacy single-bucket snapshot.
		if (buckets.Count == 0 && result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
		{
			var id = legacy.TryGetProperty("limitId", out var limitId) && limitId.ValueKind == JsonValueKind.String ? limitId.GetString()! : "codex";
			buckets.Add((id, legacy));
		}

		var windows = new List<UsageWindow>();
		foreach (var (bucketId, snapshot) in buckets)
		foreach (var slot in (string[])["primary", "secondary"])
		{
			if (!snapshot.TryGetProperty(slot, out var window) || window.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var id = $"{bucketId}/{slot}";
			windows.Add(UsageWindowFactory.Create(
				id,
				window.TryGetProperty("usedPercent", out var used) ? UsageWindowFactory.ReadNumber(used) : null,
				window.TryGetProperty("resetsAt", out var resetsAt) ? UsageWindowFactory.ParseReset(resetsAt, id) : null,
				window.TryGetProperty("windowDurationMins", out var duration) ? UsageWindowFactory.ReadNumber(duration) : null));
		}

		return windows;
	}
}