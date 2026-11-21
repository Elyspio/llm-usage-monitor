namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     A request allowance renewed each month, such as the GitHub Copilot premium requests. Its usage share is also reported
///     as a window, so the history and the reset detection apply unchanged.
/// </summary>
/// <param name="Plan">The subscription plan, e.g. <c>business</c>.</param>
/// <param name="Entitlement">The requests included in the month.</param>
/// <param name="Remaining">The included requests left.</param>
/// <param name="OverageCount">The requests made beyond the entitlement, billed apart.</param>
/// <param name="OveragePermitted">Whether requests beyond the entitlement are allowed.</param>
/// <param name="Unlimited">Whether the plan has no limit.</param>
/// <param name="ResetsAt">The start of the next month of the allowance.</param>
public sealed record MonthlyQuota(
	string? Plan,
	double Entitlement,
	double Remaining,
	double OverageCount,
	bool OveragePermitted,
	bool Unlimited,
	DateTimeOffset? ResetsAt);

public static class ProviderNames
{
	public static string Label(this Provider provider) => provider switch
	{
		Provider.Claude => "Claude",
		Provider.Codex => "Codex",
		Provider.Copilot => "Copilot",
		_ => provider.ToString()
	};

	/// <summary>Whether a prompt can open a window of the provider; a monitored-only provider is never triggered.</summary>
	public static bool IsTriggerable(this Provider provider) => provider is Provider.Claude or Provider.Codex;

	/// <summary>The CLI command that signs the service account in again.</summary>
	public static string LoginCommand(this Provider provider) => provider switch
	{
		Provider.Claude => "claude auth login",
		Provider.Codex => "codex login --device-auth",
		Provider.Copilot => "copilot login",
		_ => provider.ToString()
	};
}
