using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;

namespace LlmUsageMonitor.Core.Services;

public sealed class DashboardService(
	IProviderStateRepository states,
	ITriggerRunRepository runs,
	ISettingsService settingsService,
	TimeProvider time) : IDashboardService
{
	public const int RecentTriggerCount = 10;

	public async Task<DashboardSnapshot> GetDashboard(CancellationToken cancellationToken = default)
	{
		var settings = await settingsService.Get(cancellationToken);
		var now = time.GetUtcNow();

		var providers = new List<ProviderDashboard>();
		foreach (var provider in Enum.GetValues<Provider>())
		{
			var state = await states.Get(provider, cancellationToken);
			var running = await runs.GetRunning(provider, cancellationToken);
			var nextCheck = state.PendingResetCheck is { } pending && pending.RunAt > now ? pending.RunAt : (DateTimeOffset?)null;

			providers.Add(new ProviderDashboard(
				provider,
				state.LastReading,
				state.LastReading?.TriggerWindow?.Id,
				settings.Triggers.For(provider).AutoEnabled,
				settings.Polling.For(provider),
				nextCheck,
				running,
				new ProviderHealth(
					state.LastSuccessAt,
					state.LastFailure,
					state.ConsecutiveFailures,
					state.BackoffUntil is { } until && until > now ? until : null,
					state.ActiveAlerts,
					state.TokenExpiresAt,
					state.RefreshTokenExpiresAt)));
		}

		return new DashboardSnapshot(providers, await runs.GetRecent(RecentTriggerCount, cancellationToken));
	}
}

public sealed class HistoryService(IUsageSnapshotRepository snapshots, ITriggerRunRepository runs, TimeProvider time) : IHistoryService
{
	public async Task<UsageHistory> Get(Provider? provider, string? windowId, TimeSpan range, CancellationToken cancellationToken)
	{
		var to = time.GetUtcNow();
		var from = to - range;
		var series = await snapshots.GetHistory(provider, windowId, from, to, cancellationToken);
		var triggerRuns = await runs.GetBetween(provider, from, to, cancellationToken);
		return new UsageHistory(from, to, series, triggerRuns);
	}
}
