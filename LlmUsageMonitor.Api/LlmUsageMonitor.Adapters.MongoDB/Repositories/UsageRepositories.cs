using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class UsageSnapshotRepository(IMongoDatabase database) : IUsageSnapshotRepository
{
	private readonly IMongoCollection<UsageSnapshotDocument> _snapshots = database.GetCollection<UsageSnapshotDocument>(Collections.UsageSnapshots);

	public Task Add(Provider provider, UsageReading reading, CancellationToken cancellationToken)
	{
		var documents = reading.Windows.Select(window => new UsageSnapshotDocument
		{
			FetchedAt = reading.FetchedAt.ToUtc(),
			Meta = new SnapshotMeta { Provider = provider, WindowId = window.Id },
			UsedPercent = window.UsedPercent,
			ResetsAt = window.ResetsAt.ToUtc(),
			WindowDurationMinutes = window.WindowDurationMinutes,
		});

		return _snapshots.InsertManyAsync(documents, cancellationToken: cancellationToken);
	}

	public async Task<IReadOnlyList<UsageSeries>> GetHistory(Provider? provider, string? windowId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
	{
		var filter = Builders<UsageSnapshotDocument>.Filter;
		var query = filter.Gte(snapshot => snapshot.FetchedAt, from.ToUtc()) & filter.Lte(snapshot => snapshot.FetchedAt, to.ToUtc());
		if (provider is { } p) query &= filter.Eq(snapshot => snapshot.Meta.Provider, p);
		if (windowId is not null) query &= filter.Eq(snapshot => snapshot.Meta.WindowId, windowId);

		var snapshots = await _snapshots.Find(query).SortBy(snapshot => snapshot.FetchedAt).ToListAsync(cancellationToken);

		return snapshots
			.GroupBy(snapshot => (snapshot.Meta.Provider, snapshot.Meta.WindowId))
			.OrderBy(group => group.Key.Provider).ThenBy(group => group.Key.WindowId, StringComparer.Ordinal)
			.Select(group => new UsageSeries(
				group.Key.Provider,
				group.Key.WindowId,
				group.Select(snapshot => new UsagePoint(snapshot.FetchedAt.ToOffset(), snapshot.UsedPercent, snapshot.ResetsAt.ToOffset())).ToList()))
			.ToList();
	}
}

internal sealed class ResetRepository(IMongoDatabase database) : IResetRepository
{
	private readonly IMongoCollection<ResetDocument> _resets = database.GetCollection<ResetDocument>(Collections.Resets);

	public async Task<ResetEvent> Add(Provider provider, string windowId, DateTimeOffset detectedAt, double usedBefore, double usedAfter, DateTimeOffset? previousResetsAt, CancellationToken cancellationToken)
	{
		var document = new ResetDocument
		{
			Provider = provider,
			WindowId = windowId,
			DetectedAt = detectedAt.ToUtc(),
			UsedPercentBefore = usedBefore,
			UsedPercentAfter = usedAfter,
			PreviousResetsAt = previousResetsAt.ToUtc(),
		};
		await _resets.InsertOneAsync(document, cancellationToken: cancellationToken);
		return document.ToDomain();
	}

	public async Task<ResetEvent?> GetLast(Provider provider, string windowId, CancellationToken cancellationToken)
	{
		var document = await _resets.Find(reset => reset.Provider == provider && reset.WindowId == windowId)
			.SortByDescending(reset => reset.DetectedAt)
			.FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}
}
