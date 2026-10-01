using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class TriggerRunRepository(IMongoDatabase database) : ITriggerRunRepository
{
	private readonly IMongoCollection<TriggerRunDocument> _runs = database.GetCollection<TriggerRunDocument>(Collections.TriggerRuns);

	public async Task<TriggerRun?> TryStartAutomatic(Provider provider, string cycleKey, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		var document = NewRun(provider, false, cycleKey, model, startedAt);
		try
		{
			await _runs.InsertOneAsync(document, cancellationToken: cancellationToken);
			return document.ToDomain();
		}
		catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
		{
			// The unique index proves this cycle already had its automatic trigger: only a failed run whose retry is due starts again.
		}

		var filter = Builders<TriggerRunDocument>.Filter;
		var retry = await _runs.FindOneAndUpdateAsync(
			filter.Eq(run => run.Provider, provider)
			& filter.Eq(run => run.CycleKey, cycleKey)
			& filter.Eq(run => run.Manual, false)
			& filter.Eq(run => run.Status, TriggerStatus.Failed)
			& filter.Lte(run => run.NextRetryAt, startedAt.ToUtc()),
			new PipelineUpdateDefinition<TriggerRunDocument>(new BsonDocument[]
			{
				new("$set", new BsonDocument
				{
					["status"] = nameof(TriggerStatus.Running),
					["model"] = new BsonDocument("$literal", model),
					["startedAt"] = startedAt.UtcDateTime,
					// Runs stored before retries existed have no attempts field: they count as one.
					["attempts"] = new BsonDocument("$add", new BsonArray { new BsonDocument("$max", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$attempts", 1 }), 1 }), 1 }),
					["endedAt"] = BsonNull.Value,
					["errorCode"] = BsonNull.Value,
					["error"] = BsonNull.Value,
					["nextRetryAt"] = BsonNull.Value
				})
			}),
			new() { ReturnDocument = ReturnDocument.After },
			cancellationToken);
		return retry?.ToDomain();
	}

	public async Task<TriggerRun> StartManual(Provider provider, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		var document = NewRun(provider, true, null, model, startedAt);
		await _runs.InsertOneAsync(document, cancellationToken: cancellationToken);
		return document.ToDomain();
	}

	public async Task<TriggerRun> Complete(string id, TriggerStatus status, DateTimeOffset endedAt, string? errorCode, string? error, DateTimeOffset? nextRetryAt,
		CancellationToken cancellationToken)
	{
		var update = Builders<TriggerRunDocument>.Update
			.Set(run => run.Status, status)
			.Set(run => run.EndedAt, endedAt.ToUtc())
			.Set(run => run.ErrorCode, errorCode)
			.Set(run => run.Error, error)
			.Set(run => run.NextRetryAt, nextRetryAt.ToUtc());
		var document = await _runs.FindOneAndUpdateAsync(
			run => run.Id == Parse(id),
			update,
			new() { ReturnDocument = ReturnDocument.After },
			cancellationToken);
		return document?.ToDomain() ?? throw new ResourceNotFoundException($"Trigger run {id} does not exist.");
	}

	public async Task<TriggerRun?> Get(string id, CancellationToken cancellationToken)
	{
		if (!ObjectId.TryParse(id, out var objectId))
		{
			return null;
		}

		var document = await _runs.Find(run => run.Id == objectId).FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}

	public async Task<TriggerRun?> GetRunning(Provider provider, CancellationToken cancellationToken)
	{
		var document = await _runs.Find(run => run.Provider == provider && run.Status == TriggerStatus.Running)
			.SortByDescending(run => run.StartedAt)
			.FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}

	public async Task<IReadOnlyList<TriggerRun>> GetRecent(int count, CancellationToken cancellationToken)
	{
		var documents = await _runs.Find(FilterDefinition<TriggerRunDocument>.Empty)
			.SortByDescending(run => run.StartedAt)
			.Limit(count)
			.ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}

	public async Task<IReadOnlyList<TriggerRun>> GetBetween(Provider? provider, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
	{
		var filter = Builders<TriggerRunDocument>.Filter;
		var query = filter.Gte(run => run.StartedAt, from.ToUtc()) & filter.Lte(run => run.StartedAt, to.ToUtc());
		if (provider is { } p)
		{
			query &= filter.Eq(run => run.Provider, p);
		}

		var documents = await _runs.Find(query).SortBy(run => run.StartedAt).ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}

	public async Task<IReadOnlyList<TriggerRun>> GetAllRunning(CancellationToken cancellationToken)
	{
		var documents = await _runs.Find(run => run.Status == TriggerStatus.Running).ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}

	private static TriggerRunDocument NewRun(Provider provider, bool manual, string? cycleKey, string model, DateTimeOffset startedAt)
	{
		return new()
		{
			Id = ObjectId.GenerateNewId(),
			Provider = provider,
			Manual = manual,
			CycleKey = cycleKey,
			Model = model,
			Status = TriggerStatus.Running,
			StartedAt = startedAt.ToUtc(),
			Attempts = 1
		};
	}

	private static ObjectId Parse(string id)
	{
		return ObjectId.TryParse(id, out var objectId)
			? objectId
			: throw new ResourceNotFoundException($"Trigger run {id} does not exist.");
	}
}