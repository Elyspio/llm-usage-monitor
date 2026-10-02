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
	private static readonly FilterDefinition<SettingsDocument> Global = Builders<SettingsDocument>.Filter.Eq(settings => settings.Id, SettingsDocument.GlobalId);

	private readonly IMongoCollection<SettingsDocument> _settings = database.GetCollection<SettingsDocument>(Collections.Settings);

	public async Task<AppSettings?> Find(CancellationToken cancellationToken)
	{
		var document = await _settings.Find(Global).FirstOrDefaultAsync(cancellationToken);
		return document?.ToDomain();
	}

	public async Task Initialize(AppSettings defaults, CancellationToken cancellationToken)
	{
		try
		{
			await _settings.InsertOneAsync(SettingsDocument.FromDomain(defaults), cancellationToken: cancellationToken);
		}
		catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
		{
			// Created meanwhile: the stored settings win.
		}
	}

	public Task SavePolling(PollingSettings polling, CancellationToken cancellationToken)
	{
		return Update(Builders<SettingsDocument>.Update
			.Set(settings => settings.ClaudeIntervalMinutes, polling.ClaudeIntervalMinutes)
			.Set(settings => settings.CodexIntervalMinutes, polling.CodexIntervalMinutes), cancellationToken);
	}

	public Task SaveTriggers(TriggerSettings triggers, CancellationToken cancellationToken)
	{
		return Update(Builders<SettingsDocument>.Update
			.Set(settings => settings.ClaudeTrigger, ProviderTriggerDocument.FromDomain(triggers.Claude))
			.Set(settings => settings.CodexTrigger, ProviderTriggerDocument.FromDomain(triggers.Codex)), cancellationToken);
	}

	public Task SaveNotifications(NotificationSettings notifications, CancellationToken cancellationToken)
	{
		var document = NotificationsDocument.FromDomain(notifications);
		return Update(Builders<SettingsDocument>.Update
			.Set(settings => settings.Notifications.Url, document.Url)
			.Set(settings => settings.Notifications.Topic, document.Topic)
			.Set(settings => settings.Notifications.ProtectedToken, document.ProtectedToken)
			.Set(settings => settings.Notifications.ProviderEvents, document.ProviderEvents)
			.Set(settings => settings.Notifications.ReadFailureThreshold, document.ReadFailureThreshold)
			.Set(settings => settings.Notifications.CredentialExpiryAlertDays, document.CredentialExpiryAlertDays)
			.Unset(settings => settings.Notifications.LegacyEvents), cancellationToken);
	}

	public Task SaveSendFailure(NotificationSendFailure? failure, CancellationToken cancellationToken)
	{
		return Update(Builders<SettingsDocument>.Update.Set(settings => settings.Notifications.LastSendFailure, NotificationsDocument.FailureFromDomain(failure)), cancellationToken);
	}

	private async Task Update(UpdateDefinition<SettingsDocument> update, CancellationToken cancellationToken)
	{
		var result = await _settings.UpdateOneAsync(Global, update, cancellationToken: cancellationToken);
		if (result.MatchedCount == 0)
		{
			throw new InvalidOperationException("The settings are not initialized.");
		}
	}
}
