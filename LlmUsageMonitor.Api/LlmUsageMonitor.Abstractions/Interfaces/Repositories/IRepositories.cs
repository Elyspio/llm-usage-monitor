using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Abstractions.Interfaces.Repositories;

public interface IUsageSnapshotRepository
{
	Task Add(Provider provider, UsageReading reading, CancellationToken cancellationToken);

	Task<IReadOnlyList<UsageSeries>> GetHistory(Provider? provider, string? windowId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public interface IResetRepository
{
	Task<ResetEvent> Add(Provider provider, string windowId, DateTimeOffset detectedAt, double usedBefore, double usedAfter, DateTimeOffset? previousResetsAt, CancellationToken cancellationToken);

	Task<ResetEvent?> GetLast(Provider provider, string windowId, CancellationToken cancellationToken);
}

public interface ITriggerRunRepository
{
	/// <summary>Starts an automatic run, or returns <c>null</c> when the cycle already had one.</summary>
	Task<TriggerRun?> TryStartAutomatic(Provider provider, string cycleKey, string model, DateTimeOffset startedAt, CancellationToken cancellationToken);

	Task<TriggerRun> StartManual(Provider provider, string model, DateTimeOffset startedAt, CancellationToken cancellationToken);

	Task<TriggerRun> Complete(string id, TriggerStatus status, DateTimeOffset endedAt, string? errorCode, string? error, CancellationToken cancellationToken);

	Task<TriggerRun?> Get(string id, CancellationToken cancellationToken);

	Task<TriggerRun?> GetRunning(Provider provider, CancellationToken cancellationToken);

	Task<IReadOnlyList<TriggerRun>> GetRecent(int count, CancellationToken cancellationToken);

	Task<IReadOnlyList<TriggerRun>> GetBetween(Provider? provider, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

	/// <summary>Marks the runs left running by a previous process as failed.</summary>
	Task<long> FailRunning(DateTimeOffset endedAt, string errorCode, string error, CancellationToken cancellationToken);
}

public interface IProviderStateRepository
{
	/// <summary>Returns the stored state, or an empty one.</summary>
	Task<ProviderState> Get(Provider provider, CancellationToken cancellationToken);

	Task Save(ProviderState state, CancellationToken cancellationToken);
}

public interface ISettingsRepository
{
	Task<AppSettings?> Find(CancellationToken cancellationToken);

	Task Save(AppSettings settings, CancellationToken cancellationToken);
}
