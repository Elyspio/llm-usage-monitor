using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     Creates the time-series collection (30 day retention) and the indexes, including the automatic trigger guard.
/// </summary>
internal sealed class MongoStorageInitializer(IMongoDatabase database) : IStorageInitializer
{
	public static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(30);

	public async Task Initialize(CancellationToken cancellationToken)
	{
		using var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
		var existing = await cursor.ToListAsync(cancellationToken);

		if (!existing.Contains(Collections.UsageSnapshots))
		{
			await database.CreateCollectionAsync(Collections.UsageSnapshots, new CreateCollectionOptions
			{
				TimeSeriesOptions = new TimeSeriesOptions("fetchedAt", "meta", TimeSeriesGranularity.Minutes),
				ExpireAfter = SnapshotRetention,
			}, cancellationToken);
		}

		var resets = database.GetCollection<ResetDocument>(Collections.Resets);
		await resets.Indexes.CreateOneAsync(new CreateIndexModel<ResetDocument>(
			Builders<ResetDocument>.IndexKeys.Ascending(reset => reset.Provider).Ascending(reset => reset.WindowId).Descending(reset => reset.DetectedAt)), cancellationToken: cancellationToken);

		var runs = database.GetCollection<TriggerRunDocument>(Collections.TriggerRuns);
		await runs.Indexes.CreateManyAsync(
		[
			// At most one automatic trigger per provider and cycle: a duplicate insert fails.
			new CreateIndexModel<TriggerRunDocument>(
				Builders<TriggerRunDocument>.IndexKeys.Ascending(run => run.Provider).Ascending(run => run.CycleKey),
				new CreateIndexOptions<TriggerRunDocument>
				{
					Name = "automatic_cycle_guard",
					Unique = true,
					PartialFilterExpression = Builders<TriggerRunDocument>.Filter.Eq(run => run.Manual, false),
				}),
			new CreateIndexModel<TriggerRunDocument>(Builders<TriggerRunDocument>.IndexKeys.Descending(run => run.StartedAt)),
		], cancellationToken);
	}
}
