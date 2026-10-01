namespace LlmUsageMonitor.Abstractions.Helpers;

/// <summary>
///     Tags of the health checks: <c>/health/live</c> runs none, <c>/health/ready</c> runs the <see cref="Ready" /> ones.
/// </summary>
public static class HealthCheckTags
{
	public const string Ready = "ready";
}
