using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Reads the Codex limits with <c>account/rateLimits/read</c>; never starts a thread or a model turn.
/// </summary>
internal sealed class CodexUsageReader(IOptions<CodexOptions> options) : IUsageReader
{
	public Provider Provider => Provider.Codex;

	public async Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ReadTimeoutSeconds));
		try
		{
			await using var server = await CodexAppServer.Start(options.Value, timeout.Token);
			var result = await server.Request("account/rateLimits/read", new { }, timeout.Token);
			return CodexUsageParser.Parse(result);
		}
		catch (CodexRpcException exception)
		{
			var code = exception.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("login", StringComparison.OrdinalIgnoreCase)
				? ProviderErrorCodes.AuthExpired
				: ProviderErrorCodes.FetchFailed;
			throw new ProviderException(code, "Codex rejected the account request. Check `codex login status` on the service host.", exception);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCodes.Timeout, "Codex usage request timed out.");
		}
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