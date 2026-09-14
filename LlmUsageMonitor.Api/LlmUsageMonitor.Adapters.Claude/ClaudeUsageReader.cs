using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     One authenticated GET on the undocumented <c>/api/oauth/usage</c> endpoint with the CLI access token.
/// </summary>
internal sealed class ClaudeUsageReader(IHttpClientFactory httpClients, IOptions<ClaudeOptions> options, TimeProvider time) : IUsageReader
{
	public Provider Provider => Provider.Claude;

	public async Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken)
	{
		var credentials = await ClaudeCredentialsFile.Read(options.Value.ResolveCredentialsPath(), cancellationToken);
		if (credentials.ExpiresAt is { } expiresAt && expiresAt <= time.GetUtcNow())
		{
			throw new ProviderException(ProviderErrorCodes.AuthExpired, "The Claude CLI login has expired. Refresh it through the Claude CLI.");
		}

		using var request = new HttpRequestMessage(HttpMethod.Get, "api/oauth/usage");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
		request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ReadTimeoutSeconds));
		try
		{
			using var response = await httpClients.CreateClient(ClaudeAdapterModule.HttpClientName).SendAsync(request, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				var code = response.StatusCode switch
				{
					HttpStatusCode.Unauthorized => ProviderErrorCodes.AuthExpired,
					HttpStatusCode.Forbidden => ProviderErrorCodes.AccessDenied,
					HttpStatusCode.TooManyRequests => ProviderErrorCodes.RateLimited,
					_ => ProviderErrorCodes.HttpError,
				};
				throw new ProviderException(code, $"Claude usage endpoint returned HTTP {(int)response.StatusCode}.");
			}

			await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
			return ClaudeUsageParser.Parse(document.RootElement);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCodes.Timeout, "Claude usage request timed out.");
		}
		catch (HttpRequestException exception)
		{
			throw new ProviderException(ProviderErrorCodes.FetchFailed, "Unable to reach the Claude usage endpoint.", exception);
		}
		catch (JsonException exception)
		{
			throw new ProviderException(ProviderErrorCodes.InvalidResponse, "Claude returned an invalid usage document.", exception);
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
			throw new ProviderException(ProviderErrorCodes.InvalidResponse, "Expected a usage object.");
		}

		var windows = new List<UsageWindow>();
		foreach (var property in root.EnumerateObject())
		{
			if (property.Name == "extra_usage" || property.Value.ValueKind != JsonValueKind.Object) continue;
			if (!property.Value.TryGetProperty("utilization", out var utilization)) continue;
			if (DurationOf(property.Name) is not { } duration) continue;

			windows.Add(UsageWindowFactory.Create(
				property.Name,
				UsageWindowFactory.ReadNumber(utilization),
				property.Value.TryGetProperty("resets_at", out var resetsAt) ? UsageWindowFactory.ParseReset(resetsAt, property.Name) : null,
				duration));
		}

		return windows;
	}

	private static int? DurationOf(string id) => id == "five_hour" ? 300 : id.StartsWith("seven_day", StringComparison.Ordinal) ? 10_080 : null;
}
