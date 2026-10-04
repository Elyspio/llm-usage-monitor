using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;

namespace LlmUsageMonitor.Core.Services;

public sealed class DashboardService(
	IProviderStateRepository states,
	ITriggerRunRepository runs,
	ISettingsService settingsService,
	TimeProvider time, IResetCreditRunRepository creditRuns) : IDashboardService
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

			providers.Add(new(
				provider,
				state.LastReading,
				state.LastReading?.TriggerWindow?.Id,
				settings.Triggers.For(provider).AutoEnabled,
				settings.Polling.For(provider),
				nextCheck,
				running,
				new(
					state.LastSuccessAt,
					state.LastFailure,
					state.ConsecutiveFailures,
					state.BackoffUntil is { } until && until > now ? until : null,
					state.ActiveAlerts,
					state.TokenExpiresAt,
					state.RefreshTokenExpiresAt))
			{
				ResetCredits = await CreditDashboard(state, settings.ResetCredits.For(provider), now, cancellationToken)
			});
		}

		return new(providers, await runs.GetRecent(RecentTriggerCount, cancellationToken));
	}

	private async Task<ResetCreditDashboard> CreditDashboard(ProviderState state, ProviderResetCreditSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
	{
		var recent = await creditRuns.GetRecent(state.Provider, 10, cancellationToken);
		var pending = (await creditRuns.GetPending(state.Provider, cancellationToken)).Where(run => run.Manual || settings.AutoEnabled)
			.OrderBy(run => run.NextRetryAt ?? now).FirstOrDefault();
		ResetCredit? next = null;
		var candidates = ResetCreditRules.Ordered((state.ResetCredits?.Credits ?? []).Where(credit => ResetCreditRules.Available(credit, now)));
		foreach (var credit in candidates)
		{
			if (settings.AutoEnabled && await creditRuns.GetAutomatic(ResetCreditRules.AutomaticKey(state.Provider, credit), cancellationToken) is { }) continue;
			next = credit;
			break;
		}
		DateTimeOffset? at = pending is { } ? pending.NextRetryAt ?? now : settings.AutoEnabled && next?.ExpiresAt is { } expiry
			? (expiry.AddMinutes(-settings.BeforeExpiryMinutes) > now ? expiry.AddMinutes(-settings.BeforeExpiryMinutes) : now) : null;
		return new(state.ResetCredits, settings, pending?.CreditId ?? next?.Id, at, recent);
	}
}

public sealed class HistoryService(IUsageSnapshotRepository snapshots, ITriggerRunRepository runs, TimeProvider time) : IHistoryService
{
	/// <summary>The chart resolution: 5 minutes up to a day (288 points), an hour beyond (168 points for a week).</summary>
	public static TimeSpan BucketFor(TimeSpan range)
	{
		return range <= TimeSpan.FromDays(1) ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1);
	}

	public async Task<UsageHistory> Get(Provider? provider, string? windowId, TimeSpan range, CancellationToken cancellationToken)
	{
		var to = time.GetUtcNow();
		var from = to - range;
		var series = await snapshots.GetHistory(provider, windowId, from, to, BucketFor(range), cancellationToken);
		var triggerRuns = await runs.GetBetween(provider, from, to, cancellationToken);
		return new(from, to, series, triggerRuns);
	}
}
