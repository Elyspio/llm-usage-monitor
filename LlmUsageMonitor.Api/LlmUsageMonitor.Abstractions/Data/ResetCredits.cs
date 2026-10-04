namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>A provider reading, including the earned reset credits when exposed by the provider.</summary>
public sealed record ProviderUsage(IReadOnlyList<UsageWindow> Windows, ResetCreditBalance? ResetCredits);

/// <summary>The authoritative count may exceed the details returned by the provider; null means unknown.</summary>
public sealed record ResetCreditBalance(int? AvailableCount, IReadOnlyList<ResetCredit>? Credits, string? UnavailableReason = null);

/// <summary>One grant can contain several resets. Eligibility and affected windows come from the provider.</summary>
public sealed record ResetCredit(string Id, int RemainingUses, DateTimeOffset? ExpiresAt, DateTimeOffset? GrantedAt,
	string? Title, bool IsUsable, bool RequiresLimit, IReadOnlyList<string> WindowIds, DateTimeOffset? StartsAt = null);

/// <summary>A provider-confirmed outcome. Only Succeeded means a reset was consumed or replayed idempotently.</summary>
public sealed record ResetCreditResult(bool Succeeded, string Outcome, bool Retryable = false, DateTimeOffset? RetryAt = null);

public enum ResetCreditRunStatus { Running, Succeeded, Refused, Failed }

/// <summary>A durable logical redemption. Its ID is also the provider idempotency key, reused on every retry.</summary>
public sealed record ResetCreditRun(string Id, Provider Provider, string CreditId, bool Manual, string? AutomaticKey,
	DateTimeOffset StartedAt, DateTimeOffset? ExpiresAt, ResetCreditRunStatus Status, int Attempts = 0,
	DateTimeOffset? EndedAt = null, DateTimeOffset? NextRetryAt = null, string? Outcome = null,
	IReadOnlyList<UsageWindow>? Before = null, IReadOnlyList<UsageWindow>? After = null);

/// <summary>The manual request ID is a UUID generated once at confirmation and retained across HTTP retries.</summary>
public sealed record ConsumeResetCreditRequest(string CreditId, string IdempotencyKey);

public sealed record ResetCreditSettings(ProviderResetCreditSettings Claude, ProviderResetCreditSettings Codex)
{
	public static ResetCreditSettings Default => new(new(false, 60), new(false, 60));
	public ProviderResetCreditSettings For(Provider provider) => provider == Provider.Claude ? Claude : Codex;
}

public sealed record ProviderResetCreditSettings(bool AutoEnabled, int BeforeExpiryMinutes);

/// <summary>The decision shown beside the last known credits, not a promise of eligibility at execution time.</summary>
public sealed record ResetCreditDashboard(ResetCreditBalance? Balance, ProviderResetCreditSettings Settings,
	string? NextCreditId, DateTimeOffset? NextAttemptAt, IReadOnlyList<ResetCreditRun> RecentRuns);
