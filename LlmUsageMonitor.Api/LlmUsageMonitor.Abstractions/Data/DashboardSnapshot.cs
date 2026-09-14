namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     A monitored LLM subscription provider.
/// </summary>
public enum Provider
{
	Claude,
	Codex,
}

/// <summary>
///     The aggregate displayed by the dashboard.
/// </summary>
/// <param name="Providers">The state of every monitored provider.</param>
/// <param name="RecentTriggerRuns">The ten latest triggers, newest first.</param>
public sealed record DashboardSnapshot(IReadOnlyList<ProviderDashboard> Providers, IReadOnlyList<TriggerRun> RecentTriggerRuns);

/// <summary>
///     The dashboard state of one provider.
/// </summary>
/// <param name="Provider">The provider.</param>
/// <param name="LastReading">The last valid reading, possibly older than the last failure.</param>
/// <param name="TriggerWindowId">The shortest window, which drives the automatic trigger.</param>
/// <param name="AutoTriggerEnabled">Whether the automatic trigger is enabled.</param>
/// <param name="PollIntervalMinutes">The reading interval.</param>
/// <param name="NextAutoTriggerAt">When the post-reset check will run, if one is scheduled.</param>
/// <param name="RunningTrigger">The trigger in progress, if any.</param>
/// <param name="Health">The health block.</param>
public sealed record ProviderDashboard(
	Provider Provider,
	UsageReading? LastReading,
	string? TriggerWindowId,
	bool AutoTriggerEnabled,
	int PollIntervalMinutes,
	DateTimeOffset? NextAutoTriggerAt,
	TriggerRun? RunningTrigger,
	ProviderHealth Health);
