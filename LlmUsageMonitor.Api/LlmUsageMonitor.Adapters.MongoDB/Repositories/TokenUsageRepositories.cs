using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class TokenUsageRepository(IMongoDatabase database) : ITokenUsageRepository
{
	private readonly IMongoCollection<TokenUsageDocument> _buckets = database.GetCollection<TokenUsageDocument>(Collections.TokenUsage);

	public Task Upsert(IReadOnlyList<TokenUsageBucket> buckets, CancellationToken cancellationToken)
	{
		var writes = buckets
			.Select(TokenUsageDocument.FromDomain)
			.Select(document => new ReplaceOneModel<TokenUsageDocument>(Builders<TokenUsageDocument>.Filter.Eq(bucket => bucket.Id, document.Id), document) { IsUpsert = true })
			.ToList();

		return _buckets.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
	}

	public async Task<IReadOnlyList<TokenUsageBucket>> Get(DateTimeOffset from, DateTimeOffset to, string? machineId, CancellationToken cancellationToken)
	{
		var filter = Builders<TokenUsageDocument>.Filter;
		var query = filter.Gte(bucket => bucket.Hour, from.ToUtc()) & filter.Lt(bucket => bucket.Hour, to.ToUtc());
		if (machineId is { })
		{
			query &= filter.Eq(bucket => bucket.MachineId, machineId);
		}

		var documents = await _buckets.Find(query).ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}
}

internal sealed class UsageMachineRepository(IMongoDatabase database) : IUsageMachineRepository
{
	private readonly IMongoCollection<UsageMachineDocument> _machines = database.GetCollection<UsageMachineDocument>(Collections.UsageMachines);

	public Task Save(UsageMachine machine, CancellationToken cancellationToken)
	{
		return _machines.ReplaceOneAsync(
			document => document.Id == machine.Id,
			new UsageMachineDocument { Id = machine.Id, Name = machine.Name, LastUploadAt = machine.LastUploadAt.ToUtc() },
			new ReplaceOptions { IsUpsert = true },
			cancellationToken);
	}

	public async Task<IReadOnlyList<UsageMachine>> GetAll(CancellationToken cancellationToken)
	{
		var documents = await _machines.Find(FilterDefinition<UsageMachineDocument>.Empty).ToListAsync(cancellationToken);
		return documents.Select(document => new UsageMachine(document.Id, document.Name, document.LastUploadAt.ToOffset())).ToList();
	}
}

internal sealed class ModelPriceRepository(IMongoDatabase database) : IModelPriceRepository
{
	private readonly IMongoCollection<ModelPriceDocument> _prices = database.GetCollection<ModelPriceDocument>(Collections.ModelPrices);

	public Task Save(IReadOnlyList<ModelPrice> prices, CancellationToken cancellationToken)
	{
		var writes = prices
			.Select(ModelPriceDocument.FromDomain)
			.Select(document => new ReplaceOneModel<ModelPriceDocument>(Builders<ModelPriceDocument>.Filter.Eq(price => price.Id, document.Id), document) { IsUpsert = true })
			.ToList();

		return _prices.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
	}

	public async Task<IReadOnlyList<ModelPrice>> Find(IReadOnlyCollection<string> models, CancellationToken cancellationToken)
	{
		if (models.Count == 0)
		{
			return [];
		}

		var documents = await _prices.Find(Builders<ModelPriceDocument>.Filter.In(price => price.Id, models)).ToListAsync(cancellationToken);
		return documents.Select(document => document.ToDomain()).ToList();
	}

	public async Task<bool> Any(CancellationToken cancellationToken)
	{
		return await _prices.Find(FilterDefinition<ModelPriceDocument>.Empty).Limit(1).AnyAsync(cancellationToken);
	}
}
