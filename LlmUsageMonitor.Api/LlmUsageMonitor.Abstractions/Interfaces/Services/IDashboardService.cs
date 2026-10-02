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

	/// <summary>
	///     On start: fails the runs a previous process left running (<c>INTERRUPTED</c>); an automatic one may be retried at
	///     once, by the next reading of its cycle. Returns the number of runs.
	/// </summary>
	Task<int> RecoverInterrupted(CancellationToken cancellationToken);
}

/// <summary>
///     Keeps the Claude token valid by letting the CLI refresh it shortly before it expires.
/// </summary>
public interface IClaudeKeepAlive
{
	/// <summary>
	///     Refreshes the token if needed, then returns the state with the up-to-date expiry and the next keep-alive scheduled
	///     four minutes before it. Throws <c>AUTH_EXPIRED</c> when the token cannot be refreshed.
	/// </summary>
	Task<ProviderState> EnsureFresh(ProviderState state, CancellationToken cancellationToken);

	/// <summary>The scheduled job: refreshes the token and records the outcome in the provider health.</summary>
	Task Run(CancellationToken cancellationToken);
}

public interface ISettingsService
{
	Task<AppSettings> Get(CancellationToken cancellationToken);

	Task<PollingSettings> UpdatePolling(PollingSettings polling, CancellationToken cancellationToken);

	Task<TriggerSettings> UpdateTriggers(TriggerSettings triggers, CancellationToken cancellationToken);

	Task<NotificationSettingsView> GetNotifications(CancellationToken cancellationToken);

	Task<NotificationSettingsView> UpdateNotifications(NotificationSettingsUpdate update, CancellationToken cancellationToken);
}

/// <summary>
///     Token usage uploaded by the workstations: storage with its cost, and the report of the Usage page.
/// </summary>
public interface ITokenUsageService
{
	/// <summary>Validates, prices and stores the buckets, then records the workstation. Throws a <c>RequestValidationException</c> on invalid input.</summary>
	Task<TokenUsageUploadResult> Upload(TokenUsageUpload upload, CancellationToken cancellationToken);

	/// <summary>Returns the usage of the period, grouped by hour or by day of <paramref name="timeZone" /> (IANA id, UTC when omitted).</summary>
	Task<TokenUsageReport> Get(TokenUsageRange range, string? machineId, string? timeZone, CancellationToken cancellationToken);
}

/// <summary>
///     Keeps the model prices up to date from the public price table.
/// </summary>
public interface IModelPriceService
{
	Task Refresh(CancellationToken cancellationToken);
}

public interface INotificationService
{
	/// <summary>
	///     Sends an event if it is enabled; delivery failures (timeouts included) are recorded, never thrown.
	/// </summary>
	Task<NotificationOutcome> Notify(NotificationKind kind, Provider provider, string detail, CancellationToken cancellationToken);

	/// <summary>Sends a test message and throws when delivery fails.</summary>
	Task SendTest(CancellationToken cancellationToken);
}