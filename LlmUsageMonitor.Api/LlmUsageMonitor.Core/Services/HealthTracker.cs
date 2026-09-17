using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     Provider health: failures, rate limiting backoff and error alerts sent once per failure streak.
/// </summary>
public interface IHealthTracker
{
	Task<ProviderState> RecordSuccess(ProviderState state, DateTimeOffset now, CancellationToken cancellationToken);

	Task<ProviderState> RecordFailure(ProviderState state, ProviderException exception, DateTimeOffset now, int readFailureThreshold, CancellationToken cancellationToken);
}

public sealed class HealthTracker(INotificationService notifications) : IHealthTracker
{
	public async Task<ProviderState> RecordSuccess(ProviderState state, DateTimeOffset now, CancellationToken cancellationToken)
	{
		if (state.ActiveAlerts.Count > 0)
		{
			await notifications.Notify(NotificationKind.Recovered, state.Provider, "Les lectures réussissent de nouveau.", cancellationToken);
		}

		return state with { ConsecutiveFailures = 0, BackoffLevel = 0, BackoffUntil = null, ActiveAlerts = [] };
	}

	public async Task<ProviderState> RecordFailure(ProviderState state, ProviderException exception, DateTimeOffset now, int readFailureThreshold, CancellationToken cancellationToken)
	{
		var failure = new ProviderFailure(exception.Code, exception.Message, now);

		if (exception.Code == ProviderErrorCodes.RateLimited)
		{
			// No immediate retry: 15, then 30, then 60 minutes. A 429 does not count as a failed reading.
			var level = Math.Min(state.BackoffLevel + 1, UsageRules.BackoffSteps.Count);
			return state with { LastFailure = failure, BackoffLevel = level, BackoffUntil = now + UsageRules.BackoffSteps[level - 1] };
		}

		var next = state with { LastFailure = failure, ConsecutiveFailures = state.ConsecutiveFailures + 1 };
		NotificationKind? alert = exception.Code == ProviderErrorCodes.AuthExpired
			? NotificationKind.AuthExpired
			: next.ConsecutiveFailures >= readFailureThreshold
				? NotificationKind.ReadFailed
				: null;

		if (alert is not { } kind || next.ActiveAlerts.Contains(kind))
		{
			return next;
		}

		await notifications.Notify(kind, state.Provider, exception.Message, cancellationToken);
		return next with { ActiveAlerts = [.. next.ActiveAlerts, kind] };
	}
}