using System.Globalization;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Abstractions.Helpers;

/// <summary>
///     Validates the raw values of a provider window, with the rules of the original TypeScript readers.
/// </summary>
public static class UsageWindowFactory
{
	public static UsageWindow Create(string id, double? usedPercent, DateTimeOffset? resetsAt, double? durationMinutes)
	{
		if (usedPercent is not { } used || !double.IsFinite(used) || used < 0)
		{
			throw new ProviderException(ProviderErrorCodes.InvalidResponse, $"Invalid usage percentage for {id}.");
		}

		if (durationMinutes is { } duration && (!double.IsFinite(duration) || duration <= 0))
		{
			throw new ProviderException(ProviderErrorCodes.InvalidResponse, $"Invalid window duration for {id}.");
		}

		return new(id, used, resetsAt, durationMinutes is { } minutes ? (int)Math.Round(minutes) : null);
	}

	/// <summary>
	///     Reads a reset time given as Unix seconds or as an ISO 8601 string; <c>null</c> when absent.
	/// </summary>
	public static DateTimeOffset? ParseReset(JsonElement value, string id)
	{
		return value.ValueKind switch
		{
			JsonValueKind.Undefined or JsonValueKind.Null => null,
			JsonValueKind.Number when value.TryGetDouble(out var seconds) && double.IsFinite(seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000)),
			JsonValueKind.String when DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) => date.ToUniversalTime(),
			_ => throw new ProviderException(ProviderErrorCodes.InvalidResponse, $"Invalid reset time for {id}.")
		};
	}

	public static double? ReadNumber(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
	}
}