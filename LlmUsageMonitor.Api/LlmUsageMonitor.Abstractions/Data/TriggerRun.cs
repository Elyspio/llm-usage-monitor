namespace LlmUsageMonitor.Abstractions.Data;

public enum TriggerStatus
{
	Running,
	Succeeded,
	Failed
}

/// <summary>
///     A trigger: the minimal prompt sent to a provider to open its inactive windows.
/// </summary>
/// <param name="Id">The run identifier.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Manual"><c>true</c> when forced from the application, <c>false</c> when started after a reset.</param>
/// <param name="CycleKey">The cycle an automatic trigger belongs to; <c>null</c> for manual triggers.</param>
/// <param name="Model">The model used by the prompt.</param>
/// <param name="Status">The run status.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="EndedAt">When the run ended, if it did.</param>
/// <param name="ErrorCode">The error code of a failed run.</param>
/// <param name="Error">The raw error message of a failed run.</param>
public sealed record TriggerRun(
	string Id,
	Provider Provider,
	bool Manual,
	string? CycleKey,
	string Model,
	TriggerStatus Status,
	DateTimeOffset StartedAt,
	DateTimeOffset? EndedAt,
	string? ErrorCode,
	string? Error)
{
	public double? DurationMs => EndedAt is { } ended ? (ended - StartedAt).TotalMilliseconds : null;
}