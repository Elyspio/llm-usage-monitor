using System.Globalization;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class UsageSnapshotRepository(IMongoDatabase database) : IUsageSnapshotRepository
{
	private readonly IMongoCollection<BsonDocument> _raw = database.GetCollection<BsonDocument>(Collections.UsageSnapshots);
	private readonly IMongoCollection<UsageSnapshotDocument> _snapshots = database.GetCollection<UsageSnapshotDocument>(Collections.UsageSnapshots);

	public Task Add(Provider provider, UsageReading reading, CancellationToken cancellationToken)
	{
		var documents = reading.Windows.Select(window => new UsageSnapshotDocument
		{
			FetchedAt = reading.FetchedAt.ToUtc(),
			Meta = new() { Provider = provider, WindowId = window.Id },
			UsedPercent = window.UsedPercent,
			ResetsAt = window.ResetsAt.ToUtc(),
			WindowDurationMinutes = window.WindowDurationMinutes
		});

		return _snapshots.InsertManyAsync(documents, cancellationToken: cancellationToken);
	}

	/// <summary>
	///     Aggregated by MongoDB: one point per window and bucket, the last reading of the bucket, so a long range never loads
	///     every reading.
	/// </summary>
	public async Task<IReadOnlyList<UsageSeries>> GetHistory(Provider? provider, string? windowId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket,
		CancellationToken cancellationToken)
	{
		var match = new BsonDocument("fetchedAt", new BsonDocument { ["$gte"] = from.UtcDateTime, ["$lte"] = to.UtcDateTime });
		if (provider is { } p)
		{
			match["meta.provider"] = p.ToString();
		}

		if (windowId is { })
		{
			match["meta.windowId"] = windowId;
		}

		var binMinutes = Math.Max(1, (int)bucket.TotalMinutes);
		BsonDocument[] pipeline =
		[
			new("$match", match),
			new("$sort", new BsonDocument("fetchedAt", 1)),
			new("$group", new BsonDocument
			{
				["_id"] = new BsonDocument
				{
					["provider"] = "$meta.provider",
					["windowId"] = "$meta.windowId",
					["bucket"] = new BsonDocument("$dateTrunc", new BsonDocument { ["date"] = "$fetchedAt", ["unit"] = "minute", ["binSize"] = binMinutes })
				},
				["fetchedAt"] = new BsonDocument("$last", "$fetchedAt"),
				["usedPercent"] = new BsonDocument("$last", "$usedPercent"),
				["resetsAt"] = new BsonDocument("$last", "$resetsAt")
			}),
			new("$sort", new BsonDocument("fetchedAt", 1))
		];

		var points = await _raw.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);

		return points
			.Select(point => (
				Provider: Enum.Parse<Provider>(point["_id"]["provider"].AsString),
				WindowId: point["_id"]["windowId"].AsString,
				Point: new UsagePoint(
					point["fetchedAt"].ToUniversalTime().ToOffset(),
					point["usedPercent"].ToDouble(),
					point["resetsAt"].IsBsonNull ? null : point["resetsAt"].ToUniversalTime().ToOffset())))
			.GroupBy(point => (point.Provider, point.WindowId))
			.OrderBy(group => group.Key.Provider).ThenBy(group => group.Key.WindowId, StringComparer.Ordinal)
			.Select(group => new UsageSeries(group.Key.Provider, group.Key.WindowId, group.Select(point => point.Point).ToList()))
			.ToList();
	}
}

internal sealed class ResetRepository(IMongoDatabase database) : IResetRepository
{
	private readonly IMongoCollection<ResetDocument> _resets = database.GetCollection<ResetDocument>(Collections.Resets);

	public async Task<ResetEvent?> TryAdd(Provider provider, string windowId, DateTimeOffset previousFetchedAt, DateTimeOffset detectedAt, double usedBefore, double usedAfter,
		DateTimeOffset? previousResetsAt, CancellationToken cancellationToken)
	{
		var document = new ResetDocument
		{
			Key = string.Create(CultureInfo.InvariantCulture, $"{provider}|{windowId}|{previousFetchedAt.UtcDateTime:O}"),
			Provider = provider,
			WindowId = windowId,
			DetectedAt = detectedAt.ToUtc(),
			UsedPercentBefore = usedBefore,
			UsedPercentAfter = usedAfter,
			PreviousResetsAt = previousResetsAt.ToUtc()
		};
		try
		{
			await _resets.InsertOneAsync(document, cancellationToken: cancellationToken);
			return document.ToDomain();
		}
		catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
		{
			return null;
		}
	}

	public async Task<ResetEvent?> GetLast(Provider provider, string windowId, CancellationToken cancellationToken)
	{
		var document = await _resets.Find(reset => reset.Provider == provider && reset.WindowId == windowId)
			.SortByDescending(reset => reset.DetectedAt)
			.FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}
}