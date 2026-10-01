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

public interface ITokenUsageRepository
{
	/// <summary>Writes each bucket in place of the stored one with the same workstation, hour, provider and model.</summary>
	Task Upsert(IReadOnlyList<TokenUsageBucket> buckets, CancellationToken cancellationToken);

	/// <summary>Returns the buckets whose hour starts in [<paramref name="from" />, <paramref name="to" />).</summary>
	Task<IReadOnlyList<TokenUsageBucket>> Get(DateTimeOffset from, DateTimeOffset to, string? machineId, CancellationToken cancellationToken);
}

public interface IUsageMachineRepository
{
	Task Save(UsageMachine machine, CancellationToken cancellationToken);

	Task<IReadOnlyList<UsageMachine>> GetAll(CancellationToken cancellationToken);
}

public interface IModelPriceRepository
{
	/// <summary>Writes each price in place of the stored one; models absent from the list keep their last price.</summary>
	Task Save(IReadOnlyList<ModelPrice> prices, CancellationToken cancellationToken);

	/// <summary>Returns the stored prices among the given model names.</summary>
	Task<IReadOnlyList<ModelPrice>> Find(IReadOnlyCollection<string> models, CancellationToken cancellationToken);

	Task<bool> Any(CancellationToken cancellationToken);
}