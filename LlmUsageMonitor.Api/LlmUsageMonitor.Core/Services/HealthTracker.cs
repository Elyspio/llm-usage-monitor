using System.Globalization;
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

	/// <summary>
	///     Warns once, <paramref name="alertDays" /> days ahead, that the CLI login (refresh token) is about to expire: past it,
	///     only a new login on the host restores the readings.
	/// </summary>
	Task<ProviderState> CheckCredentialExpiry(ProviderState state, DateTimeOffset now, int alertDays, CancellationToken cancellationToken);
}

public sealed class HealthTracker(INotificationService notifications) : IHealthTracker
{
	public async Task<ProviderState> RecordSuccess(ProviderState state, DateTimeOffset now, CancellationToken cancellationToken)
	{
		if (state.ActiveAlerts.Count > 0)
		{
			await notifications.Notify(NotificationKind.Recovered, state.Provider, "Les lectures réussissent de nouveau.", cancellationToken);
		}

		return state with { ConsecutiveFailures = 0, ConsecutiveRateLimits = 0, BackoffLevel = 0, BackoffUntil = null, ActiveAlerts = [] };
	}

	public async Task<ProviderState> RecordFailure(ProviderState state, ProviderException exception, DateTimeOffset now, int readFailureThreshold, CancellationToken cancellationToken)
	{
		var failure = new ProviderFailure(exception.Code, exception.Message, now);

		if (exception.Code == ProviderErrorCode.RateLimited)
		{
			// No immediate retry: 15, then 30, then 60 minutes. A 429 is not a failed reading, but a streak of them alerts.
			var level = Math.Min(state.BackoffLevel + 1, UsageRules.BackoffSteps.Count);
			var limited = state with
			{
				LastFailure = failure,
				ConsecutiveRateLimits = state.ConsecutiveRateLimits + 1,
				BackoffLevel = level,
				BackoffUntil = now + UsageRules.BackoffSteps[level - 1]
			};
			return limited.ConsecutiveRateLimits >= readFailureThreshold
				? await Alert(limited, NotificationKind.ReadFailed, $"{limited.ConsecutiveRateLimits} lectures limitées (429) d'affilée : {exception.Message}", cancellationToken)
				: limited;
		}

		var next = state with { LastFailure = failure, ConsecutiveFailures = state.ConsecutiveFailures + 1, ConsecutiveRateLimits = 0 };
		NotificationKind? alert = exception.Code == ProviderErrorCode.AuthExpired
			? NotificationKind.AuthExpired
			: next.ConsecutiveFailures >= readFailureThreshold
				? NotificationKind.ReadFailed
				: null;

		return alert is { } kind ? await Alert(next, kind, exception.Message, cancellationToken) : next;
	}

	public async Task<ProviderState> CheckCredentialExpiry(ProviderState state, DateTimeOffset now, int alertDays, CancellationToken cancellationToken)
	{
		if (state.RefreshTokenExpiresAt is not { } expiresAt || expiresAt - now > TimeSpan.FromDays(alertDays) || state.CredentialExpiryAlertedFor == expiresAt)
		{
			return state;
		}

		var login = state.Provider == Provider.Claude ? "claude auth login" : "codex login --device-auth";
		var detail = string.Create(CultureInfo.InvariantCulture, $"La connexion du CLI expire le {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC : relancer `{login}` sur le serveur.");
		// Marked only once delivered: a warning skipped while ntfy is not configured is sent once it is.
		return await notifications.Notify(NotificationKind.AuthExpiring, state.Provider, detail, cancellationToken) == NotificationOutcome.Delivered
			? state with { CredentialExpiryAlertedFor = expiresAt }
			: state;
	}

	/// <summary>
	///     Sends an alert once per failure streak. An alert that was not delivered (failed, or skipped while ntfy is not
	///     configured or the event disabled) stays inactive: the next failed reading sends it again.
	/// </summary>
	private async Task<ProviderState> Alert(ProviderState state, NotificationKind kind, string detail, CancellationToken cancellationToken)
	{
		if (state.ActiveAlerts.Contains(kind))
		{
			return state;
		}

		return await notifications.Notify(kind, state.Provider, detail, cancellationToken) == NotificationOutcome.Delivered
			? state with { ActiveAlerts = [.. state.ActiveAlerts, kind] }
			: state;
	}
}
