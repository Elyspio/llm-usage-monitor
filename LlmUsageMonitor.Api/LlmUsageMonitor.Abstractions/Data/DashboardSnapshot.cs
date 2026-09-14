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
public sealed record DashboardSnapshot(IReadOnlyList<ProviderDashboard> Providers);

/// <summary>
///     The dashboard state of one provider.
/// </summary>
/// <param name="Provider">The provider.</param>
public sealed record ProviderDashboard(Provider Provider);
