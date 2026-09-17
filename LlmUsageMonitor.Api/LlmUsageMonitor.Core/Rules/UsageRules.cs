using System.Globalization;
using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Core.Rules;

/// <summary>
///     The reset and cycle rules of the automatic trigger.
/// </summary>
public static class UsageRules
{
	/// <summary>Rate limiting backoff steps, applied one after the other.</summary>
	public static readonly IReadOnlyList<TimeSpan> BackoffSteps = [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

	/// <summary>
	///     A reset is a drop of the used share of a window between two readings.
	/// </summary>
	public static IReadOnlyList<(UsageWindow Previous, UsageWindow Current)> DetectResets(UsageReading? previous, UsageReading current)
	{
		if (previous is null)
		{
			return [];
		}

		return current.Windows
			.Join(previous.Windows, window => window.Id, window => window.Id, (currentWindow, previousWindow) => (previousWindow, currentWindow))
			.Where(pair => pair.currentWindow.UsedPercent < pair.previousWindow.UsedPercent)
			.ToList();
	}

	/// <summary>
	///     Returns the cycle of the trigger window when it waits for its first message (0 % used and no reset time ahead), or
	///     <c>null</c> otherwise. The cycle is, by order of preference: the one already recorded while waiting, the reset
	///     time that just expired (truncated to the minute), then the last detected reset. Without any of them, no automatic
	///     trigger: the manual button covers that case.
	/// </summary>
	public static string? CycleKey(ProviderState state, UsageReading current, ResetEvent? lastReset, DateTimeOffset now)
	{
		var window = current.TriggerWindow;
		if (window is null || window.UsedPercent > 0 || window.ResetsAt > now)
		{
			return null;
		}

		return state.CurrentCycleKey
		       ?? ExpiredResetsAt(state.LastReading, window.Id, now)
		       ?? (lastReset is null ? null : $"reset:{lastReset.Id}");
	}

	private static string? ExpiredResetsAt(UsageReading? previous, string windowId, DateTimeOffset now)
	{
		var resetsAt = previous?.Windows.FirstOrDefault(window => window.Id == windowId)?.ResetsAt;
		if (resetsAt is not { } expired || expired > now)
		{
			return null;
		}

		var minute = new DateTimeOffset(expired.UtcDateTime.Year, expired.UtcDateTime.Month, expired.UtcDateTime.Day, expired.UtcDateTime.Hour, expired.UtcDateTime.Minute, 0, TimeSpan.Zero);
		return $"resets:{minute.ToString("yyyy-MM-ddTHH:mm'Z'", CultureInfo.InvariantCulture)}";
	}
}