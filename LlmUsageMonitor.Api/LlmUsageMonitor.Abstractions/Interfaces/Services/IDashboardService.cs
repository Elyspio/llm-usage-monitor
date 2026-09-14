using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Abstractions.Interfaces.Services;

/// <summary>
///     Builds the dashboard aggregate.
/// </summary>
public interface IDashboardService
{
	/// <summary>
	///     Returns the current state of every monitored provider.
	/// </summary>
	Task<DashboardSnapshot> GetDashboard(CancellationToken cancellationToken = default);
}

public interface IHistoryService
{
	/// <summary>Returns the snapshots and triggers of the last 24 hours or 7 days.</summary>
	Task<UsageHistory> Get(Provider? provider, string? windowId, TimeSpan range, CancellationToken cancellationToken);
}

/// <summary>
///     Reads a provider, stores the snapshots, detects resets and decides the automatic trigger.
/// </summary>
public interface IUsageMonitor
{
	Task Poll(Provider provider, CancellationToken cancellationToken);
}

public interface ITriggerService
{
	/// <summary>Records a manual run and queues it. Throws <c>CLI_BUSY</c> when a CLI process already runs for the provider.</summary>
	Task<TriggerRun> RequestManual(Provider provider, CancellationToken cancellationToken);

	/// <summary>Runs a queued manual run.</summary>
	Task ExecuteManual(string runId, CancellationToken cancellationToken);

	Task<TriggerRun> Get(string runId, CancellationToken cancellationToken);
}

/// <summary>
///     Keeps the Claude token valid by letting the CLI refresh it shortly before it expires.
/// </summary>
public interface IClaudeKeepAlive
{
	/// <summary>Refreshes the token if needed and returns the up-to-date expiry. Throws <c>AUTH_EXPIRED</c> when it cannot.</summary>
	Task<ClaudeTokenState> EnsureFresh(CancellationToken cancellationToken);

	/// <summary>The scheduled job: refreshes the token and records the outcome in the provider health.</summary>
	Task Run(CancellationToken cancellationToken);

	/// <summary>Records the token expiry and schedules the next keep-alive four minutes before it.</summary>
	ProviderState ScheduleNext(ProviderState state, ClaudeTokenState token);
}

public sealed record ClaudeTokenState(DateTimeOffset? ExpiresAt, DateTimeOffset? RefreshTokenExpiresAt);

public interface ISettingsService
{
	Task<AppSettings> Get(CancellationToken cancellationToken);

	Task<PollingSettings> UpdatePolling(PollingSettings polling, CancellationToken cancellationToken);

	Task<TriggerSettings> UpdateTriggers(TriggerSettings triggers, CancellationToken cancellationToken);

	Task<NotificationSettingsView> GetNotifications(CancellationToken cancellationToken);

	Task<NotificationSettingsView> UpdateNotifications(NotificationSettingsUpdate update, CancellationToken cancellationToken);
}

public interface INotificationService
{
	/// <summary>Sends an event if it is enabled; delivery failures are recorded, never thrown.</summary>
	Task Notify(NotificationKind kind, Provider provider, string detail, CancellationToken cancellationToken);

	/// <summary>Sends a test message and throws when delivery fails.</summary>
	Task SendTest(CancellationToken cancellationToken);
}
