namespace LlmUsageMonitor.Abstractions.Configurations;

/// <summary>
///     Infrastructure settings of the application (configuration files only).
/// </summary>
public sealed class AppConfig
{
	public const string Section = "App";

	/// <summary>
	///     The public origin of the dashboard, opened by a click on a notification.
	/// </summary>
	public string PublicUrl { get; init; } = "https://localhost:3000";

	/// <summary>
	///     The default of the automatic trigger when the settings are created. Disabled in development so a local run never
	///     prompts the real accounts on its own.
	/// </summary>
	public bool AutoTriggerEnabledByDefault { get; init; } = true;
}