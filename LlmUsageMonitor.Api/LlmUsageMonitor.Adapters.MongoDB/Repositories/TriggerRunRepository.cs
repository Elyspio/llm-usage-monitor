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
		var document = NewRun(provider, manual: false, cycleKey, model, startedAt);
		try
		{
			await _runs.InsertOneAsync(document, cancellationToken: cancellationToken);
			return document.ToDomain();
		}
		catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
		{
			// The unique index proves this cycle already had its automatic trigger.
			return null;
		}
	}

	public async Task<TriggerRun> StartManual(Provider provider, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		var document = NewRun(provider, manual: true, cycleKey: null, model, startedAt);
		await _runs.InsertOneAsync(document, cancellationToken: cancellationToken);
		return document.ToDomain();
	}

	public async Task<TriggerRun> Complete(string id, TriggerStatus status, DateTimeOffset endedAt, string? errorCode, string? error, CancellationToken cancellationToken)
	{
		var update = Builders<TriggerRunDocument>.Update
			.Set(run => run.Status, status)
			.Set(run => run.EndedAt, endedAt.ToUtc())
			.Set(run => run.ErrorCode, errorCode)
			.Set(run => run.Error, error);
		var document = await _runs.FindOneAndUpdateAsync(
			run => run.Id == Parse(id),
			update,
			new FindOneAndUpdateOptions<TriggerRunDocument> { ReturnDocument = ReturnDocument.After },
			cancellationToken);
		return document?.ToDomain() ?? throw new ResourceNotFoundException($"Trigger run {id} does not exist.");
	}

	public async Task<TriggerRun?> Get(string id, CancellationToken cancellationToken)
	{
		if (!ObjectId.TryParse(id, out var objectId)) return null;
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
		if (provider is { } p) query &= filter.Eq(run => run.Provider, p);

		var documents = await _runs.Find(query).SortBy(run => run.StartedAt).ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}

	public async Task<long> FailRunning(DateTimeOffset endedAt, string errorCode, string error, CancellationToken cancellationToken)
	{
		var update = Builders<TriggerRunDocument>.Update
			.Set(run => run.Status, TriggerStatus.Failed)
			.Set(run => run.EndedAt, endedAt.ToUtc())
			.Set(run => run.ErrorCode, errorCode)
			.Set(run => run.Error, error);
		var result = await _runs.UpdateManyAsync(run => run.Status == TriggerStatus.Running, update, cancellationToken: cancellationToken);
		return result.ModifiedCount;
	}

	private static TriggerRunDocument NewRun(Provider provider, bool manual, string? cycleKey, string model, DateTimeOffset startedAt) => new()
	{
		Id = ObjectId.GenerateNewId(),
		Provider = provider,
		Manual = manual,
		CycleKey = cycleKey,
		Model = model,
		Status = TriggerStatus.Running,
		StartedAt = startedAt.ToUtc(),
	};

	private static ObjectId Parse(string id) => ObjectId.TryParse(id, out var objectId)
		? objectId
		: throw new ResourceNotFoundException($"Trigger run {id} does not exist.");
}
