using System.Net;
using System.Text.Json;
using Elyspio.Utils.Results;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace LlmUsageMonitor.Adapters.Copilot;

/// <summary>
///     One authenticated GET on the undocumented <c>copilot_internal/user</c> endpoint, the one the Copilot clients read
///     their quotas from, with the CLI token.
/// </summary>
internal sealed class CopilotUsageReader(IHttpClientFactory httpClients, IOptions<CopilotOptions> options) : IUsageReader
{
	/// <summary>The environment variables, replaced by the tests.</summary>
	internal Func<string, string?> Environment { get; init; } = System.Environment.GetEnvironmentVariable;

	public Provider Provider => Provider.Copilot;

	public async Task<Result<IReadOnlyList<UsageWindow>, ProviderError>> Read(CancellationToken cancellationToken) =>
		(await ReadAccount(cancellationToken)).Map(usage => usage.Windows);

	public Task<Result<ProviderUsage, ProviderError>> ReadAccount(CancellationToken cancellationToken) => ProviderResult.Capture(() => ReadAccountOrThrow(cancellationToken));

	private async Task<ProviderUsage> ReadAccountOrThrow(CancellationToken cancellationToken)
	{
		var token = await CopilotCredentials.Read(options.Value.ResolveConfigPath(), Environment, cancellationToken);

		using var request = new HttpRequestMessage(HttpMethod.Get, "copilot_internal/user");
		request.Headers.Authorization = new("token", token);
		request.Headers.Accept.Add(new("application/json"));
		request.Headers.Add("X-GitHub-Api-Version", "2025-04-01");

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ReadTimeoutSeconds));
		try
		{
			using var response = await httpClients.CreateClient(CopilotAdapterModule.HttpClientName).SendAsync(request, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				var code = response.StatusCode switch
				{
					HttpStatusCode.Unauthorized => ProviderErrorCode.AuthExpired,
					HttpStatusCode.Forbidden or HttpStatusCode.NotFound => ProviderErrorCode.AccessDenied,
					HttpStatusCode.TooManyRequests => ProviderErrorCode.RateLimited,
					_ => ProviderErrorCode.HttpError
				};
				var message = code == ProviderErrorCode.AuthExpired
					? "GitHub rejects the Copilot CLI token. Run `copilot login` on the service host."
					: $"Copilot usage endpoint returned HTTP {(int)response.StatusCode}.";
				throw new ProviderException(code, message);
			}

			await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
			return CopilotUsageParser.Parse(document.RootElement);
		}
		catch (Exception exception) when (exception is TimeoutRejectedException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
		{
			throw new ProviderException(ProviderErrorCode.Timeout, "Copilot usage request timed out.", exception);
		}
		catch (BrokenCircuitException exception)
		{
			throw new ProviderException(ProviderErrorCode.FetchFailed, "The Copilot usage endpoint keeps failing: requests are paused for a moment.", exception);
		}
		catch (HttpRequestException exception)
		{
			throw new ProviderException(ProviderErrorCode.FetchFailed, "Unable to reach the Copilot usage endpoint.", exception);
		}
		catch (JsonException exception)
		{
			throw new ProviderException(ProviderErrorCode.InvalidResponse, "Copilot returned an invalid usage document.", exception);
		}
	}
}

/// <summary>
///     Keeps the monthly AI credits (<c>quota_snapshots.premium_interactions</c>, named after the former premium requests): one window without duration (a
///     calendar month, never a trigger window) and the credit counts. The chat and completion quotas of the Free plan are
///     ignored.
/// </summary>
public static class CopilotUsageParser
{
	public const string PremiumWindowId = "premium_interactions";

	public static ProviderUsage Parse(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new ProviderException(ProviderErrorCode.InvalidResponse, "Expected a Copilot user object.");
		}

		if (!root.TryGetProperty("quota_snapshots", out var snapshots) || snapshots.ValueKind != JsonValueKind.Object
		    || !snapshots.TryGetProperty(PremiumWindowId, out var premium) || premium.ValueKind != JsonValueKind.Object)
		{
			throw new ProviderException(ProviderErrorCode.NoUsageData, "The Copilot account reports no AI credit quota.");
		}

		var resetsAt = root.TryGetProperty("quota_reset_date_utc", out var resetUtc) ? UsageWindowFactory.ParseReset(resetUtc, PremiumWindowId)
			: root.TryGetProperty("quota_reset_date", out var reset) ? UsageWindowFactory.ParseReset(reset, PremiumWindowId)
			: null;

		var unlimited = Bool(premium, "unlimited");
		var entitlement = Number(premium, "entitlement") ?? 0;
		var remaining = Number(premium, "remaining") ?? Number(premium, "quota_remaining") ?? 0;
		var percentRemaining = Number(premium, "percent_remaining") ?? (entitlement > 0 ? remaining / entitlement * 100 : null);
		double? used = unlimited ? 0 : percentRemaining is { } percent ? Math.Clamp(100 - percent, 0, 100) : null;

		var window = UsageWindowFactory.Create(PremiumWindowId, used, resetsAt, null);
		var plan = root.TryGetProperty("copilot_plan", out var planValue) && planValue.ValueKind == JsonValueKind.String ? planValue.GetString() : null;
		var quota = new MonthlyQuota(plan, entitlement, remaining, Number(premium, "overage_count") ?? 0, Bool(premium, "overage_permitted"), unlimited, resetsAt);
		return new([window], null) { Quota = quota };
	}

	private static double? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? UsageWindowFactory.ReadNumber(value) : null;

	private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
