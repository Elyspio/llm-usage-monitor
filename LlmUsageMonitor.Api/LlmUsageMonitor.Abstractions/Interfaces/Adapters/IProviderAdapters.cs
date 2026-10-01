using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Abstractions.Interfaces.Adapters;

/// <summary>
///     Reads the usage windows of a provider. Throws a <see cref="Exceptions.ProviderException" /> on failure.
/// </summary>
public interface IUsageReader
{
	Provider Provider { get; }

	Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken);
}

/// <summary>
///     Sends the minimal prompt through the provider CLI. Throws a <see cref="Exceptions.ProviderException" /> on failure.
/// </summary>
public interface IPromptRunner
{
	Provider Provider { get; }

	Task Run(string model, CancellationToken cancellationToken);
}

/// <summary>
///     The Claude CLI login: token expiry and refresh through the CLI, never by the service itself.
/// </summary>
public interface IClaudeSession
{
	/// <summary>Reads the expiry dates from the CLI credentials file.</summary>
	Task<ClaudeTokenInfo> ReadToken(CancellationToken cancellationToken);

	/// <summary>Runs a free CLI command that refreshes the token when it expires within five minutes.</summary>
	Task RefreshThroughCli(CancellationToken cancellationToken);
}

public sealed record ClaudeTokenInfo(DateTimeOffset? ExpiresAt, DateTimeOffset? RefreshTokenExpiresAt);

/// <summary>
///     A message sent to the notification channel.
/// </summary>
public sealed record NotificationMessage(string Title, string Body, NotificationPriority Priority, IReadOnlyList<string> Tags, string? ClickUrl);

public enum NotificationPriority
{
	Default,
	High
}

public interface INotificationSender
{
	Task Send(NotificationMessage message, string serverUrl, string topic, string? token, CancellationToken cancellationToken);
}

/// <summary>
///     Background jobs: polling, post-reset checks, keep-alive and manual triggers.
/// </summary>
public interface IJobScheduler
{
	void SetPollInterval(Provider provider, int minutes);

	void EnqueuePoll(Provider provider);

	string SchedulePostResetCheck(Provider provider, DateTimeOffset runAt);

	string ScheduleKeepAlive(DateTimeOffset runAt);

	void EnqueueTrigger(string runId);

	/// <summary>Schedules the daily refresh of the model prices.</summary>
	void SchedulePriceRefresh();

	void EnqueuePriceRefresh();

	void Delete(string jobId);
}

/// <summary>
///     Downloads the per-token prices of the Anthropic and OpenAI models.
/// </summary>
public interface IModelPriceSource
{
	Task<IReadOnlyList<ModelPrice>> Fetch(CancellationToken cancellationToken);
}

/// <summary>
///     Creates collections and indexes before the application starts.
/// </summary>
public interface IStorageInitializer
{
	Task Initialize(CancellationToken cancellationToken);
}

/// <summary>
///     Encrypts secrets stored with the settings.
/// </summary>
public interface ISecretProtector
{
	string Protect(string value);

	string Unprotect(string value);
}