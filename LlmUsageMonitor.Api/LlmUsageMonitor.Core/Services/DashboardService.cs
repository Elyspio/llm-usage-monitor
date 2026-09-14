using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Services;

namespace LlmUsageMonitor.Core.Services;

public sealed class DashboardService : IDashboardService
{
	public Task<DashboardSnapshot> GetDashboard(CancellationToken cancellationToken = default)
	{
		var providers = Enum.GetValues<Provider>()
			.Select(provider => new ProviderDashboard(provider))
			.ToList();

		return Task.FromResult(new DashboardSnapshot(providers));
	}
}
