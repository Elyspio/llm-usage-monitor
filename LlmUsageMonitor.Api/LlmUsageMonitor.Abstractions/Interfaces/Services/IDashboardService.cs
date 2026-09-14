using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Abstractions.Interfaces.Services;

/// <summary>
///     Builds the dashboard aggregate.
/// </summary>
public interface IDashboardService
{
	/// <summary>
	///     Returns the current state of every monitored provider.
	/// </summary>
	Task<DashboardSnapshot> GetDashboard(CancellationToken cancellationToken = default);
}
