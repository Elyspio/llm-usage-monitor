using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>The cedar_ember flow captured from Claude Code 2.1.289. The CLI config and credentials are read only.</summary>
internal sealed class ClaudeResetCreditConsumer(IHttpClientFactory clients, IOptions<ClaudeOptions> options) : IResetCreditConsumer
{
	public Provider Provider => Provider.Claude;
	public async Task<ResetCreditResult> Consume(string creditId, string idempotencyKey, CancellationToken cancellationToken)
	{
		var settings = options.Value;
		var credentials = await ClaudeCredentialsFile.Read(settings.ResolveCredentialsPath(), cancellationToken);
		string organization;
		try
		{
			await using var stream = File.OpenRead(settings.ResolveAccountConfigPath());
			using var config = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			organization = config.RootElement.GetProperty("oauthAccount").GetProperty("organizationUuid").GetString()!;
			if (!Guid.TryParse(organization, out _)) throw new JsonException("Invalid organization UUID.");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
		{
			throw new ProviderException(ProviderErrorCodes.CredentialsUnavailable, "Cannot read the Claude OAuth organization. Set Claude:AccountConfigPath to the CLI .claude.json.", exception);
		}
		using var request = new HttpRequestMessage(HttpMethod.Post, $"api/organizations/{organization}/reset_rate_limits");
		request.Headers.Authorization = new("Bearer", credentials.AccessToken);
		request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
		request.Content = JsonContent.Create(new { program = "cedar_ember", grant_id = creditId, request_id = idempotencyKey });
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(settings.ReadTimeoutSeconds));
		try
		{
			using var response = await clients.CreateClient(ClaudeAdapterModule.HttpClientName).SendAsync(request, timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				throw new ProviderException(response.StatusCode switch
				{
					HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorCodes.AuthExpired,
					HttpStatusCode.TooManyRequests => ProviderErrorCodes.RateLimited,
					_ => ProviderErrorCodes.HttpError
				}, $"Claude reset endpoint returned HTTP {(int)response.StatusCode}.");
			}
			using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
			return ClaudeResetCreditParser.ParseResult(document.RootElement);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProviderException(ProviderErrorCodes.Timeout, "Claude reset request timed out.");
		}
	}
}

/// <summary>Provider eligibility is preserved: paid usage credits are unrelated to these earned resets.</summary>
public static class ClaudeResetCreditParser
{
	public static ResetCreditBalance? Parse(JsonElement root)
	{
		if (!root.TryGetProperty("cedar_ember", out var block) || block.ValueKind != JsonValueKind.Object) return null;
		var eligible = Flag(block, "eligible");
		if (!block.TryGetProperty("grants", out var grants) || grants.ValueKind != JsonValueKind.Array)
			return new(null, null, Text(block, "ineligible_reason"));
		var credits = grants.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object && Text(row, "id") is { Length: > 0 })
			.Select(row => new ResetCredit(Text(row, "id")!, Number(row, "resets_left"), Date(row, "ends_at"), Date(row, "starts_at"),
				Text(row, "label"), eligible && !Flag(row, "paused") && Flag(row, "usable_now"),
				!row.TryGetProperty("use_requires_limit", out var limit) || limit.ValueKind != JsonValueKind.False,
				row.TryGetProperty("clears", out var clears) && clears.ValueKind == JsonValueKind.Array
					? clears.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToList() : [], Date(row, "starts_at")))
			.ToList();
		return new(credits.Sum(credit => credit.RemainingUses), credits, eligible ? null : Text(block, "ineligible_reason") ?? "ineligible");
	}

	public static ResetCreditResult ParseResult(JsonElement root)
	{
		var outcome = Text(root, "result");
		return outcome switch
		{
			"reset" or "already_used" => new(true, outcome),
			"not_limited" or "ineligible" => new(false, outcome),
			"cooldown" => new(false, outcome, true, Date(root, "cooldown_until")),
			"unavailable" => new(false, outcome, true),
			_ => throw new ProviderException(ProviderErrorCodes.InvalidResponse, "Claude returned an unknown reset outcome; retry with the same request ID.")
		};
	}

	private static bool Flag(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
	private static int Number(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
	private static string? Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	private static DateTimeOffset? Date(JsonElement row, string name) => row.TryGetProperty(name, out var value) ? UsageWindowFactory.ParseReset(value, name) : null;
}
