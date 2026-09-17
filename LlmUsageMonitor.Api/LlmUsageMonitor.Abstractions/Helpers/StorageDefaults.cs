namespace LlmUsageMonitor.Abstractions.Helpers;

/// <summary>
///     Defaults of the MongoDB connection, shared by the adapters: the connection string of the Aspire resource carries no
///     database name.
/// </summary>
public static class StorageDefaults
{
	public const string DatabaseName = "llm-usage-monitor";
}
