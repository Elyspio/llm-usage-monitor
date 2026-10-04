namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     The behaviour settings edited from the application. Infrastructure stays in the configuration files.
/// </summary>
public sealed record AppSettings(PollingSettings Polling, TriggerSettings Triggers, NotificationSettings Notifications)
{
	public ResetCreditSettings ResetCredits { get; init; } = ResetCreditSettings.Default;
	public static AppSettings CreateDefault(bool autoTriggerEnabled)
	{
		return new(
			new(3, 3),
			new(new(autoTriggerEnabled, "haiku"), new(autoTriggerEnabled, "gpt-5.6-luna")),
			new("https://ntfy.sh", null, null, NotificationEventsByProvider.Default, 3, null, NotificationSettings.DefaultCredentialExpiryAlertDays));
	}
}

public sealed record PollingSettings(int ClaudeIntervalMinutes, int CodexIntervalMinutes)
{
	public const int MinIntervalMinutes = 1;
	public const int MaxIntervalMinutes = 60;

	/// <summary>
	///     The poll job runs on a <c>*/N</c> minute cron, which restarts at each hour: only divisors of 60 keep the gaps even
	///     (45 would run at :00 and :45, 15 minutes apart).
	/// </summary>
	public static bool DividesHour(int minutes)
	{
		return minutes is >= MinIntervalMinutes and <= MaxIntervalMinutes && MaxIntervalMinutes % minutes == 0;
	}

	/// <summary>The largest divisor of 60 not above <paramref name="minutes" />, for intervals saved before that rule.</summary>
	public static int ToHourDivisor(int minutes)
	{
		var divisor = Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);
		while (!DividesHour(divisor)) divisor--;
		return divisor;
	}

	public int For(Provider provider)
	{
		return provider == Provider.Claude ? ClaudeIntervalMinutes : CodexIntervalMinutes;
	}
}

public sealed record TriggerSettings(ProviderTriggerSettings Claude, ProviderTriggerSettings Codex)
{
	public ProviderTriggerSettings For(Provider provider)
	{
		return provider == Provider.Claude ? Claude : Codex;
	}
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
/// <param name="CredentialExpiryAlertDays">Days before the expiry of the CLI login (refresh token) when an alert is sent.</param>
public sealed record NotificationSettings(
	string Url,
	string? Topic,
	string? ProtectedToken,
	NotificationEventsByProvider Events,
	int ReadFailureThreshold,
	NotificationSendFailure? LastSendFailure,
	int CredentialExpiryAlertDays)
{
	public const int MinReadFailureThreshold = 1;
	public const int MaxReadFailureThreshold = 20;
	public const int DefaultCredentialExpiryAlertDays = 7;
	public const int MinCredentialExpiryAlertDays = 1;
	public const int MaxCredentialExpiryAlertDays = 60;
}

public sealed record NotificationEvents(bool TriggerFailed, bool AuthExpired, bool ReadFailed, bool Reset, bool TriggerSucceeded, bool Recovered,
	bool ResetCreditSucceeded = true, bool ResetCreditFailed = true)
{
	public static NotificationEvents Default => new(true, true, true, false, true, true);

	public bool IsEnabled(NotificationKind kind)
	{
		return kind switch
		{
			NotificationKind.TriggerFailed => TriggerFailed,
			// The early warning before the login expires goes with the expiry alert.
			NotificationKind.AuthExpired or NotificationKind.AuthExpiring => AuthExpired,
			NotificationKind.ReadFailed => ReadFailed,
			NotificationKind.Reset => Reset,
			NotificationKind.TriggerSucceeded => TriggerSucceeded,
			NotificationKind.Recovered => Recovered,
			NotificationKind.ResetCreditSucceeded => ResetCreditSucceeded,
			NotificationKind.ResetCreditFailed => ResetCreditFailed,
			_ => false
		};
	}
}

public sealed record NotificationEventsByProvider(NotificationEvents Claude, NotificationEvents Codex)
{
	public static NotificationEventsByProvider Default => new(NotificationEvents.Default, NotificationEvents.Default);

	public NotificationEvents For(Provider provider)
	{
		return provider == Provider.Claude ? Claude : Codex;
	}
}

public sealed record NotificationSendFailure(DateTimeOffset At, string Message);

/// <summary>
///     What became of a notification: only a delivered alert counts as sent, so a skipped one (no topic, event disabled) is
///     sent once the notifications are enabled.
/// </summary>
public enum NotificationOutcome
{
	Delivered,
	Skipped,
	Failed
}

public enum NotificationKind
{
	TriggerFailed,
	AuthExpired,
	ReadFailed,
	Reset,
	TriggerSucceeded,
	Recovered,
	AuthExpiring,
	ResetCreditSucceeded,
	ResetCreditFailed
}

/// <summary>
///     The notification settings returned by the API: the token is never sent back.
/// </summary>
public sealed record NotificationSettingsView(
	string Url,
	string? Topic,
	bool TokenDefined,
	NotificationEventsByProvider Events,
	int ReadFailureThreshold,
	NotificationSendFailure? LastSendFailure,
	int CredentialExpiryAlertDays);

/// <summary>
///     The notification settings sent by the application.
/// </summary>
/// <param name="Token">
///     The new token: <c>null</c> keeps the current one (on the same server only: a new URL needs its token again), an empty
///     string removes it.
/// </param>
/// <param name="CredentialExpiryAlertDays">Days before the expiry of the CLI login when an alert is sent.</param>
public sealed record NotificationSettingsUpdate(string Url, string? Topic, string? Token, NotificationEventsByProvider Events, int ReadFailureThreshold, int CredentialExpiryAlertDays);
