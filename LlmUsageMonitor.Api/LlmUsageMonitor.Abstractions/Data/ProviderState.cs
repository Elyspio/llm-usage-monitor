using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     The persisted state of a provider: last valid reading, health, cycle and scheduled jobs.
/// </summary>
public sealed record ProviderState(Provider Provider)
{
	/// <summary>The last successful reading, kept to display stale values and to detect resets.</summary>
	public UsageReading? LastReading { get; init; }

	public ResetCreditBalance? ResetCredits { get; init; }
	public ScheduledJob? PendingCreditCheck { get; init; }
	/// <summary>Do not wake a cycle opened by a purchased/earned reset. Cleared by genuine usage or another reset date.</summary>
	public bool CreditResetSuppressesTrigger { get; init; }
	public bool CreditResetRefreshPending { get; init; }
	public DateTimeOffset? CreditResetWindowResetsAt { get; init; }

	public DateTimeOffset? LastSuccessAt { get; init; }

	public ProviderFailure? LastFailure { get; init; }

	/// <summary>Consecutive failed readings, rate limiting excluded.</summary>
	public int ConsecutiveFailures { get; init; }

	/// <summary>Consecutive rate limited readings: past the failure threshold, they alert too.</summary>
	public int ConsecutiveRateLimits { get; init; }

	/// <summary>Rate limiting backoff step: 0 (none), then 15, 30 and 60 minutes.</summary>
	public int BackoffLevel { get; init; }

	public DateTimeOffset? BackoffUntil { get; init; }

	/// <summary>Error alerts already notified, so each one is sent once until the provider recovers.</summary>
	public IReadOnlyList<NotificationKind> ActiveAlerts { get; init; } = [];

	/// <summary>The cycle of the trigger window while it waits for its first message.</summary>
	public string? CurrentCycleKey { get; init; }

	/// <summary>The one-off check scheduled one minute after the trigger window reset.</summary>
	public ScheduledJob? PendingResetCheck { get; init; }

	/// <summary>The Claude token keep-alive, scheduled four minutes before the token expires.</summary>
	public ScheduledJob? KeepAlive { get; init; }

	public DateTimeOffset? TokenExpiresAt { get; init; }

	public DateTimeOffset? RefreshTokenExpiresAt { get; init; }

	/// <summary>The login expiry already announced by the early warning: a new login (another expiry) is announced again.</summary>
	public DateTimeOffset? CredentialExpiryAlertedFor { get; init; }
}

public sealed record ProviderFailure(ProviderErrorCode Code, string Message, DateTimeOffset At);

public sealed record ScheduledJob(string JobId, DateTimeOffset RunAt);

/// <summary>
///     The health block of the dashboard.
/// </summary>
public sealed record ProviderHealth(
	DateTimeOffset? LastSuccessAt,
	ProviderFailure? LastFailure,
	int ConsecutiveFailures,
	DateTimeOffset? BackoffUntil,
	IReadOnlyList<NotificationKind> ActiveAlerts,
	DateTimeOffset? TokenExpiresAt,
	DateTimeOffset? RefreshTokenExpiresAt);
