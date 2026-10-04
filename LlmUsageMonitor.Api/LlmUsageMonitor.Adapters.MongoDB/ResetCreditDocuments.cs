using LlmUsageMonitor.Abstractions.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace LlmUsageMonitor.Adapters.MongoDB;

internal sealed class ResetCreditDocument
{
	public string CreditId { get; set; } = null!;
	public int RemainingUses { get; set; }
	public DateTime? ExpiresAt { get; set; }
	public DateTime? GrantedAt { get; set; }
	public DateTime? StartsAt { get; set; }
	public string? Title { get; set; }
	public bool IsUsable { get; set; }
	public bool RequiresLimit { get; set; }
	public List<string> WindowIds { get; set; } = [];
	public static ResetCreditDocument FromDomain(ResetCredit credit) => new()
	{
		CreditId = credit.Id,
		RemainingUses = credit.RemainingUses,
		ExpiresAt = credit.ExpiresAt.ToUtc(),
		GrantedAt = credit.GrantedAt.ToUtc(),
		StartsAt = credit.StartsAt.ToUtc(),
		Title = credit.Title,
		IsUsable = credit.IsUsable,
		RequiresLimit = credit.RequiresLimit,
		WindowIds = [.. credit.WindowIds]
	};
	public ResetCredit ToDomain() => new(CreditId, RemainingUses, ExpiresAt.ToOffset(), GrantedAt.ToOffset(), Title, IsUsable, RequiresLimit, WindowIds, StartsAt.ToOffset());
}

internal sealed class ResetCreditBalanceDocument
{
	public int? AvailableCount { get; set; }
	public List<ResetCreditDocument>? Credits { get; set; }
	public string? UnavailableReason { get; set; }
	public static ResetCreditBalanceDocument? FromDomain(ResetCreditBalance? balance) => balance is null ? null : new()
	{
		AvailableCount = balance.AvailableCount,
		Credits = balance.Credits?.Select(ResetCreditDocument.FromDomain).ToList(),
		UnavailableReason = balance.UnavailableReason
	};
	public ResetCreditBalance ToDomain() => new(AvailableCount, Credits?.Select(credit => credit.ToDomain()).ToList(), UnavailableReason);
}

internal sealed class ProviderResetCreditSettingsDocument
{
	public bool AutoEnabled { get; set; }
	public int BeforeExpiryMinutes { get; set; } = 60;
	public static ProviderResetCreditSettingsDocument FromDomain(ProviderResetCreditSettings settings) => new() { AutoEnabled = settings.AutoEnabled, BeforeExpiryMinutes = settings.BeforeExpiryMinutes };
	public ProviderResetCreditSettings ToDomain() => new(AutoEnabled, BeforeExpiryMinutes);
}

internal sealed class ResetCreditRunDocument
{
	public string Id { get; set; } = null!;
	public Provider Provider { get; set; }
	public string CreditId { get; set; } = null!;
	public bool Manual { get; set; }
	[BsonIgnoreIfNull] public string? AutomaticKey { get; set; }
	public DateTime StartedAt { get; set; }
	public DateTime? ExpiresAt { get; set; }
	public ResetCreditRunStatus Status { get; set; }
	public int Attempts { get; set; }
	public DateTime? EndedAt { get; set; }
	public DateTime? NextRetryAt { get; set; }
	public string? Outcome { get; set; }
	public List<WindowDocument>? Before { get; set; }
	public List<WindowDocument>? After { get; set; }
	public static ResetCreditRunDocument FromDomain(ResetCreditRun run) => new()
	{
		Id = run.Id,
		Provider = run.Provider,
		CreditId = run.CreditId,
		Manual = run.Manual,
		AutomaticKey = run.AutomaticKey,
		StartedAt = run.StartedAt.ToUtc(),
		ExpiresAt = run.ExpiresAt.ToUtc(),
		Status = run.Status,
		Attempts = run.Attempts,
		EndedAt = run.EndedAt.ToUtc(),
		NextRetryAt = run.NextRetryAt.ToUtc(),
		Outcome = run.Outcome,
		Before = run.Before?.Select(WindowDocument.FromDomain).ToList(),
		After = run.After?.Select(WindowDocument.FromDomain).ToList()
	};
	public ResetCreditRun ToDomain() => new(Id, Provider, CreditId, Manual, AutomaticKey, StartedAt.ToOffset(), ExpiresAt.ToOffset(), Status, Attempts,
		EndedAt.ToOffset(), NextRetryAt.ToOffset(), Outcome, Before?.Select(window => window.ToDomain()).ToList(), After?.Select(window => window.ToDomain()).ToList());
}
