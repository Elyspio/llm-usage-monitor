using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     Creates the time-series collection and the indexes, including the automatic trigger guard, and applies the retention
///     of every collection: snapshots 30 days, resets and trigger runs 90 days (Hangfire jobs: see HangfireAdapterModule).
/// </summary>
internal sealed class MongoStorageInitializer(IMongoDatabase database, ILogger<MongoStorageInitializer> logger) : IStorageInitializer
{
	public static readonly TimeSpan SnapshotRetention = TimeSpan.FromDays(30);
	public static readonly TimeSpan ResetRetention = TimeSpan.FromDays(90);
	public static readonly TimeSpan TriggerRunRetention = TimeSpan.FromDays(90);

	private const int IndexOptionsConflict = 85;
	private const int IndexKeySpecsConflict = 86;
	private const int Unauthorized = 13;

	public async Task Initialize(CancellationToken cancellationToken)
	{
		using var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
		var existing = await cursor.ToListAsync(cancellationToken);

		if (!existing.Contains(Collections.UsageSnapshots))
		{
			await database.CreateCollectionAsync(Collections.UsageSnapshots, new()
			{
				TimeSeriesOptions = new("fetchedAt", "meta", TimeSeriesGranularity.Minutes),
				ExpireAfter = SnapshotRetention
			}, cancellationToken);
		}
		else
		{
			await ApplySnapshotRetention(cancellationToken);
		}

		var resets = database.GetCollection<ResetDocument>(Collections.Resets);
		await resets.Indexes.CreateManyAsync(
		[
			new(Builders<ResetDocument>.IndexKeys.Ascending(reset => reset.Provider).Ascending(reset => reset.WindowId).Descending(reset => reset.DetectedAt)),
			// One reset per transition: a poll replayed after an interruption does not store it twice.
			new(Builders<ResetDocument>.IndexKeys.Ascending(reset => reset.Key), new CreateIndexOptions<ResetDocument>
			{
				Name = "transition_guard",
				Unique = true,
				PartialFilterExpression = Builders<ResetDocument>.Filter.Exists(reset => reset.Key)
			})
		], cancellationToken);
		await EnsureTtl(Collections.Resets, new BsonDocument("detectedAt", 1), ResetRetention, cancellationToken);

		var runs = database.GetCollection<TriggerRunDocument>(Collections.TriggerRuns);
		await runs.Indexes.CreateOneAsync(new CreateIndexModel<TriggerRunDocument>(
			// At most one automatic trigger per provider and cycle: a duplicate insert fails.
			Builders<TriggerRunDocument>.IndexKeys.Ascending(run => run.Provider).Ascending(run => run.CycleKey),
			new CreateIndexOptions<TriggerRunDocument>
			{
				Name = "automatic_cycle_guard",
				Unique = true,
				PartialFilterExpression = Builders<TriggerRunDocument>.Filter.Eq(run => run.Manual, false)
			}), cancellationToken: cancellationToken);
		// The sort index of the journal also expires the runs (a cycle lasts a week at most: the guard stays meaningful).
		await EnsureTtl(Collections.TriggerRuns, new BsonDocument("startedAt", -1), TriggerRunRetention, cancellationToken);

		var creditRuns = database.GetCollection<ResetCreditRunDocument>(Collections.ResetCreditRuns);
		await creditRuns.Indexes.CreateOneAsync(new CreateIndexModel<ResetCreditRunDocument>(
			Builders<ResetCreditRunDocument>.IndexKeys.Ascending(run => run.AutomaticKey), new CreateIndexOptions<ResetCreditRunDocument>
			{
				Name = "automatic_credit_guard",
				Unique = true,
				PartialFilterExpression = Builders<ResetCreditRunDocument>.Filter.Exists(run => run.AutomaticKey)
			}), cancellationToken: cancellationToken);
		await creditRuns.Indexes.CreateOneAsync(new CreateIndexModel<ResetCreditRunDocument>(
			Builders<ResetCreditRunDocument>.IndexKeys.Ascending(run => run.Provider).Descending(run => run.StartedAt)), cancellationToken: cancellationToken);
		// Running requests retain their idempotency keys until resolved, even after a long outage.
		await EnsureTtl(Collections.ResetCreditRuns, new BsonDocument("endedAt", 1), TriggerRunRetention, cancellationToken);

		// Token usage is kept without expiry; the reports read it by period, for every workstation or one.
		var tokenUsage = database.GetCollection<TokenUsageDocument>(Collections.TokenUsage);
		await tokenUsage.Indexes.CreateOneAsync(new CreateIndexModel<TokenUsageDocument>(
			Builders<TokenUsageDocument>.IndexKeys.Ascending(bucket => bucket.Hour).Ascending(bucket => bucket.MachineId)), cancellationToken: cancellationToken);
	}

	/// <summary>
	///     The retention of a time-series collection is set at its creation: a collection created with another one (or none)
	///     is updated with <c>collMod</c>.
	/// </summary>
	private async Task ApplySnapshotRetention(CancellationToken cancellationToken)
	{
		using var collections = await database.ListCollectionsAsync(new() { Filter = new BsonDocument("name", Collections.UsageSnapshots) }, cancellationToken);
		var options = (await collections.SingleAsync(cancellationToken)).GetValue("options", new BsonDocument()).AsBsonDocument;
		var expected = (long)SnapshotRetention.TotalSeconds;
		if (options.TryGetValue("expireAfterSeconds", out var current) && current.IsNumeric && current.ToInt64() == expected)
		{
			return;
		}

		await CollMod(new BsonDocument { ["collMod"] = Collections.UsageSnapshots, ["expireAfterSeconds"] = expected }, cancellationToken);
	}

	/// <summary>
	///     Creates a TTL index, or turns the existing index on the same key into one (or changes its delay) with <c>collMod</c>.
	/// </summary>
	private async Task EnsureTtl(string collectionName, BsonDocument keys, TimeSpan retention, CancellationToken cancellationToken)
	{
		var collection = database.GetCollection<BsonDocument>(collectionName);
		try
		{
			await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions { ExpireAfter = retention }), cancellationToken: cancellationToken);
		}
		catch (MongoCommandException exception) when (exception.Code is IndexOptionsConflict or IndexKeySpecsConflict)
		{
			await CollMod(new BsonDocument
			{
				["collMod"] = collectionName,
				["index"] = new BsonDocument { ["keyPattern"] = keys, ["expireAfterSeconds"] = (long)retention.TotalSeconds }
			}, cancellationToken);
		}
	}

	/// <summary>
	///     <c>collMod</c> needs more than <c>readWrite</c> (<c>dbAdmin</c> on the database): without it, the application starts
	///     anyway and logs the command to run by hand.
	/// </summary>
	private async Task CollMod(BsonDocument command, CancellationToken cancellationToken)
	{
		try
		{
			await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
			logger.LogInformation("Retention applied: {Command}", command.ToJson());
		}
		catch (MongoCommandException exception) when (exception.Code == Unauthorized)
		{
			logger.LogWarning("Retention not applied, the MongoDB user may not run collMod: run db.runCommand({Command}) as an administrator", command.ToJson());
		}
	}
}
