namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     The behaviour settings edited from the application. Infrastructure stays in the configuration files.
/// </summary>
public sealed record AppSettings(PollingSettings Polling, TriggerSettings Triggers, NotificationSettings Notifications)
{
	public static AppSettings CreateDefault(bool autoTriggerEnabled) => new(
		new PollingSettings(3, 3),
		new TriggerSettings(new ProviderTriggerSettings(autoTriggerEnabled, "haiku"), new ProviderTriggerSettings(autoTriggerEnabled, "gpt-5.6-luna")),
		new NotificationSettings("https://ntfy.sh", null, null, NotificationEvents.Default, 3, null));
}

public sealed record PollingSettings(int ClaudeIntervalMinutes, int CodexIntervalMinutes)
{
	public const int MinIntervalMinutes = 1;
	public const int MaxIntervalMinutes = 60;

	public int For(Provider provider) => provider == Provider.Claude ? ClaudeIntervalMinutes : CodexIntervalMinutes;
}

public sealed record TriggerSettings(ProviderTriggerSettings Claude, ProviderTriggerSettings Codex)
{
	public ProviderTriggerSettings For(Provider provider) => provider == Provider.Claude ? Claude : Codex;
}

/// <param name="AutoEnabled">Whether the trigger starts on its own after a reset; readings and manual triggers are unaffected.</param>
/// <param name="Model">The model of the minimal prompt.</param>
public sealed record ProviderTriggerSettings(bool AutoEnabled, string Model);

/// <param name="Url">The ntfy server URL.</param>
/// <param name="Topic">The topic; notifications are disabled while it is empty.</param>
/// <param name="ProtectedToken">The access token, encrypted at rest.</param>
/// <param name="Events">The notified events.</param>
/// <param name="ReadFailureThreshold">Consecutive failed readings before an alert.</param>
/// <param name="LastSendFailure">The last delivery failure, shown in the settings.</param>
public sealed record NotificationSettings(
	string Url,
	string? Topic,
	string? ProtectedToken,
	NotificationEvents Events,
	int ReadFailureThreshold,
	NotificationSendFailure? LastSendFailure)
{
	public const int MinReadFailureThreshold = 1;
	public const int MaxReadFailureThreshold = 20;
}

public sealed record NotificationEvents(bool TriggerFailed, bool AuthExpired, bool ReadFailed, bool Reset, bool TriggerSucceeded, bool Recovered)
{
	public static NotificationEvents Default => new(true, true, true, false, true, true);

	public bool IsEnabled(NotificationKind kind) => kind switch
	{
		NotificationKind.TriggerFailed => TriggerFailed,
		NotificationKind.AuthExpired => AuthExpired,
		NotificationKind.ReadFailed => ReadFailed,
		NotificationKind.Reset => Reset,
		NotificationKind.TriggerSucceeded => TriggerSucceeded,
		NotificationKind.Recovered => Recovered,
		_ => false,
	};
}

public sealed record NotificationSendFailure(DateTimeOffset At, string Message);

public enum NotificationKind
{
	TriggerFailed,
	AuthExpired,
	ReadFailed,
	Reset,
	TriggerSucceeded,
	Recovered,
}

/// <summary>
///     The notification settings returned by the API: the token is never sent back.
/// </summary>
public sealed record NotificationSettingsView(
	string Url,
	string? Topic,
	bool TokenDefined,
	NotificationEvents Events,
	int ReadFailureThreshold,
	NotificationSendFailure? LastSendFailure);

/// <summary>
///     The notification settings sent by the application.
/// </summary>
/// <param name="Token">The new token: <c>null</c> keeps the current one, an empty string removes it.</param>
public sealed record NotificationSettingsUpdate(string Url, string? Topic, string? Token, NotificationEvents Events, int ReadFailureThreshold);
