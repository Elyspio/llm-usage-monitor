using System.Net;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     One authenticated GET on the undocumented <c>/api/oauth/usage</c> endpoint with the CLI access token.
/// </summary>
internal sealed class ClaudeUsageReader(IHttpClientFactory httpClients, IOptions<ClaudeOptions> options, TimeProvider time) : IUsageReader
{
	public Provider Provider => Provider.Claude;

	public async Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken) => (await ReadAccount(cancellationToken)).Windows;

	public async Task<ProviderUsage> ReadAccount(CancellationToken cancellationToken)
	{
		var credentials = await ClaudeCredentialsFile.Read(options.Value.ResolveCredentialsPath(), cancellationToken);
		if (credentials.ExpiresAt is { } expiresAt && expiresAt <= time.GetUtcNow())
		{
			throw new ProviderException(ProviderErrorCode.AuthExpired, "The Claude CLI login has expired. Refresh it through the Claude CLI.");
		}

		// Claude Code 2.1.289 requests this block explicitly; a missing block remains unknown, not zero.
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/oauth/usage?cedar_ember=1&skip_spend=1");
		request.Headers.Authorization = new("Bearer", credentials.AccessToken);
		request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
		request.Headers.Accept.Add(new("application/json"));

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ReadTimeoutSeconds));
		try
		{
			using var response = await httpClients.CreateClient(ClaudeAdapterModule.HttpClientName).SendAsync(request, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				var code = response.StatusCode switch
				{
					HttpStatusCode.Unauthorized => ProviderErrorCode.AuthExpired,
					HttpStatusCode.Forbidden => ProviderErrorCode.AccessDenied,
					HttpStatusCode.TooManyRequests => ProviderErrorCode.RateLimited,
					_ => ProviderErrorCode.HttpError
				};
				throw new ProviderException(code, $"Claude usage endpoint returned HTTP {(int)response.StatusCode}.");
			}

			await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
			return new(ClaudeUsageParser.Parse(document.RootElement), ClaudeResetCreditParser.Parse(document.RootElement));
		}
		catch (Exception exception) when (exception is TimeoutRejectedException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			throw new ProviderException(ProviderErrorCode.Timeout, "Claude usage request timed out.", exception);
		}
		catch (BrokenCircuitException exception)
		{
			throw new ProviderException(ProviderErrorCode.FetchFailed, "The Claude usage endpoint keeps failing: requests are paused for a moment.", exception);
		}
		catch (HttpRequestException exception)
		{
			throw new ProviderException(ProviderErrorCode.FetchFailed, "Unable to reach the Claude usage endpoint.", exception);
		}
		catch (JsonException exception)
		{
			throw new ProviderException(ProviderErrorCode.InvalidResponse, "Claude returned an invalid usage document.", exception);
		}
	}
}

/// <summary>
///     Port of the TypeScript reader. Only the known windows are kept: <c>five_hour</c> (300 min) and <c>seven_day*</c>
///     (10 080 min); unknown ones (e.g. <c>nimbus_quill</c>), null ones and monetary <c>extra_usage</c> are ignored.
/// </summary>
public static class ClaudeUsageParser
{
	public static IReadOnlyList<UsageWindow> Parse(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new ProviderException(ProviderErrorCode.InvalidResponse, "Expected a usage object.");
		}

		var windows = new List<UsageWindow>();
		foreach (var property in root.EnumerateObject())
		{
			if (property.Name == "extra_usage" || property.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (!property.Value.TryGetProperty("utilization", out var utilization))
			{
				continue;
			}

			if (DurationOf(property.Name) is not { } duration)
			{
				continue;
			}

			windows.Add(UsageWindowFactory.Create(
				property.Name,
				UsageWindowFactory.ReadNumber(utilization),
				property.Value.TryGetProperty("resets_at", out var resetsAt) ? UsageWindowFactory.ParseReset(resetsAt, property.Name) : null,
				duration));
		}

		if (ReadFable(root) is { } fable && windows.All(window => window.Id != FableWindowId))
		{
			windows.Add(fable);
		}

		return windows;
	}

	/// <summary>The id the earned resets give the Fable weekly limit in <c>clears</c>.</summary>
	public const string FableWindowId = "seven_day_overage_included";

	/// <summary>
	///     The Fable weekly limit has no block of its own: it is the <c>weekly_scoped</c> entry of <c>limits</c> whose model is
	///     Fable. Other scoped models are ignored, their id in the earned resets being unknown.
	/// </summary>
	private static UsageWindow? ReadFable(JsonElement root)
	{
		if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (var limit in limits.EnumerateArray())
		{
			if (limit.ValueKind == JsonValueKind.Object
			    && limit.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "weekly_scoped"
			    && limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
			    && scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
			    && model.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String
			    && string.Equals(name.GetString(), "Fable", StringComparison.OrdinalIgnoreCase)
			    && limit.TryGetProperty("percent", out var percent) && percent.ValueKind == JsonValueKind.Number)
			{
				return UsageWindowFactory.Create(FableWindowId, UsageWindowFactory.ReadNumber(percent),
					limit.TryGetProperty("resets_at", out var resetsAt) ? UsageWindowFactory.ParseReset(resetsAt, FableWindowId) : null, 10_080);
			}
		}

		return null;
	}

	private static int? DurationOf(string id)
	{
		return id == "five_hour" ? 300 : id.StartsWith("seven_day", StringComparison.Ordinal) ? 10_080 : null;
	}
}
