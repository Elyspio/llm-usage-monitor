using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Core.Rules;

internal static class ResetCreditRules
{
	public static bool Available(ResetCredit credit, DateTimeOffset now) => credit.IsUsable && credit.RemainingUses > 0
		&& (credit.StartsAt is null || credit.StartsAt <= now) && (credit.ExpiresAt is null || credit.ExpiresAt > now);

	public static string AutomaticKey(Provider provider, ResetCredit credit) => $"{provider}:{credit.Id}:{credit.RemainingUses}:{credit.GrantedAt:O}";

	public static IOrderedEnumerable<ResetCredit> Ordered(IEnumerable<ResetCredit> credits) => credits
		.OrderBy(credit => credit.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(credit => credit.GrantedAt).ThenBy(credit => credit.Id, StringComparer.Ordinal);
}
