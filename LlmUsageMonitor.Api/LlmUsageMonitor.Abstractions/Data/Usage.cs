using System.Text.Json.Serialization;

namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     A rolling usage allowance of a provider, as reported by a reading.
/// </summary>
/// <param name="Id">The provider window identifier, e.g. <c>five_hour</c> or <c>codex/primary</c>.</param>
/// <param name="UsedPercent">The consumed share of the window, from 0 to 100.</param>
/// <param name="ResetsAt">The reset time, or <c>null</c> when the provider announces none (window not started).</param>
/// <param name="WindowDurationMinutes">The window length, or <c>null</c> when unknown.</param>
public sealed record UsageWindow(string Id, double UsedPercent, DateTimeOffset? ResetsAt, int? WindowDurationMinutes)
{
	/// <summary>
	///     The remaining share of the window. 100 means unused.
	/// </summary>
	public double RemainingPercent => Math.Max(0, 100 - UsedPercent);
}

/// <summary>
///     The state of every window of a provider, returned by a successful reading.
/// </summary>
public sealed record UsageReading(DateTimeOffset FetchedAt, IReadOnlyList<UsageWindow> Windows)
{
	/// <summary>
	///     The shortest window of the provider: its return to 0 % allows the automatic trigger.
	/// </summary>
	[JsonIgnore]
	public UsageWindow? TriggerWindow => Windows
		.Where(window => window.WindowDurationMinutes is not null)
		.MinBy(window => window.WindowDurationMinutes);
}

/// <summary>
///     One stored snapshot of a window, used by the history chart.
/// </summary>
public sealed record UsagePoint(DateTimeOffset FetchedAt, double UsedPercent, DateTimeOffset? ResetsAt)
{
	public double RemainingPercent => Math.Max(0, 100 - UsedPercent);
}

/// <summary>
///     The snapshots of one window over a period.
/// </summary>
public sealed record UsageSeries(Provider Provider, string WindowId, IReadOnlyList<UsagePoint> Points);

/// <summary>
///     The history chart content: snapshots per window and the triggers of the period.
/// </summary>
public sealed record UsageHistory(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<UsageSeries> Series, IReadOnlyList<TriggerRun> TriggerRuns);

/// <summary>
///     A reset: the used share of a window dropped between two readings.
/// </summary>
public sealed record ResetEvent(
	string Id,
	Provider Provider,
	string WindowId,
	DateTimeOffset DetectedAt,
	double UsedPercentBefore,
	double UsedPercentAfter,
	DateTimeOffset? PreviousResetsAt);
