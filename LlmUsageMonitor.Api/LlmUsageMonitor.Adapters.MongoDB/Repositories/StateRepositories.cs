using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB.Repositories;

internal sealed class ProviderStateRepository(IMongoDatabase database) : IProviderStateRepository
{
	private readonly IMongoCollection<ProviderStateDocument> _states = database.GetCollection<ProviderStateDocument>(Collections.ProviderStates);

	public async Task<ProviderState> Get(Provider provider, CancellationToken cancellationToken)
	{
		var document = await _states.Find(state => state.Provider == provider).FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain() ?? new ProviderState(provider);
	}

	public Task Save(ProviderState state, CancellationToken cancellationToken)
	{
		return _states.ReplaceOneAsync(
			document => document.Provider == state.Provider,
			ProviderStateDocument.FromDomain(state),
			new ReplaceOptions { IsUpsert = true },
			cancellationToken);
	}
}

internal sealed class SettingsRepository(IMongoDatabase database) : ISettingsRepository
{
	private readonly IMongoCollection<SettingsDocument> _settings = database.GetCollection<SettingsDocument>(Collections.Settings);

	public async Task<AppSettings?> Find(CancellationToken cancellationToken)
	{
		var document = await _settings.Find(settings => settings.Id == SettingsDocument.GlobalId).FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}

	public Task Save(AppSettings settings, CancellationToken cancellationToken)
	{
		return _settings.ReplaceOneAsync(
			document => document.Id == SettingsDocument.GlobalId,
			SettingsDocument.FromDomain(settings),
			new ReplaceOptions { IsUpsert = true },
			cancellationToken);
	}
}