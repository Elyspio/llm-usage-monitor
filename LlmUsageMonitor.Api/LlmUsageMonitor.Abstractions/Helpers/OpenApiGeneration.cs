using System.Reflection;

namespace LlmUsageMonitor.Abstractions.Helpers;

/// <summary>
///     The OpenAPI document is generated on build by running the application entry point without starting the host: services
///     that connect at construction time (Hangfire storage) are left out then.
/// </summary>
public static class OpenApiGeneration
{
	public static bool IsRunning { get; } = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}