using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class ResetCreditRunRepository(IMongoDatabase database) : IResetCreditRunRepository
{
	private readonly IMongoCollection<ResetCreditRunDocument> _runs = database.GetCollection<ResetCreditRunDocument>(Collections.ResetCreditRuns);
	public async Task<ResetCreditRun> Start(ResetCreditRun run, CancellationToken cancellationToken)
	{
		try { await _runs.InsertOneAsync(ResetCreditRunDocument.FromDomain(run), cancellationToken: cancellationToken); return run; }
		catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
		{
			var existing = await _runs.Find(document => document.Id == run.Id || (run.AutomaticKey != null && document.AutomaticKey == run.AutomaticKey)).SingleAsync(cancellationToken);
			return existing.ToDomain();
		}
	}
	public async Task Save(ResetCreditRun run, CancellationToken cancellationToken)
	{
		var result = await _runs.ReplaceOneAsync(document => document.Id == run.Id, ResetCreditRunDocument.FromDomain(run), cancellationToken: cancellationToken);
		if (result.MatchedCount == 0) throw new InvalidOperationException("The reset redemption was not durably started.");
	}
	public async Task<ResetCreditRun?> Get(string id, CancellationToken cancellationToken) => (await _runs.Find(document => document.Id == id).FirstOrDefaultAsync(cancellationToken))?.ToDomain();
	public async Task<ResetCreditRun?> GetAutomatic(string automaticKey, CancellationToken cancellationToken) =>
		(await _runs.Find(document => document.AutomaticKey == automaticKey).FirstOrDefaultAsync(cancellationToken))?.ToDomain();
	public async Task<IReadOnlyList<ResetCreditRun>> GetPending(Provider provider, CancellationToken cancellationToken) =>
		(await _runs.Find(document => document.Provider == provider && document.Status == ResetCreditRunStatus.Running).SortBy(document => document.StartedAt).ToListAsync(cancellationToken))
		.Select(document => document.ToDomain()).ToList();
	public async Task<IReadOnlyList<ResetCreditRun>> GetRecent(Provider provider, int count, CancellationToken cancellationToken) =>
		(await _runs.Find(document => document.Provider == provider).SortByDescending(document => document.StartedAt).Limit(count).ToListAsync(cancellationToken))
		.Select(document => document.ToDomain()).ToList();
}
