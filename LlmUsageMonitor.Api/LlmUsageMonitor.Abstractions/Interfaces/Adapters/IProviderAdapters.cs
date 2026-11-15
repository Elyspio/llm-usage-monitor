using Elyspio.Utils.Results;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Abstractions.Interfaces.Adapters;

/// <summary>
///     Reads the usage windows of a provider; a failure is a <see cref="ProviderError" />.
/// </summary>
public interface IUsageReader
{
	Provider Provider { get; }

	Task<Result<IReadOnlyList<UsageWindow>, ProviderError>> Read(CancellationToken cancellationToken);

	/// <summary>Older adapters can expose windows alone; null credits never mean zero available credits.</summary>
	async Task<Result<ProviderUsage, ProviderError>> ReadAccount(CancellationToken cancellationToken) =>
		(await Read(cancellationToken)).Map(windows => new ProviderUsage(windows, null));
}

/// <summary>Redeems an earned reset, with a durable idempotency key; never sends a model prompt.</summary>
public interface IResetCreditConsumer
{
	Provider Provider { get; }
	Task<Result<ResetCreditResult, ProviderError>> Consume(string creditId, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>
///     Sends the minimal prompt through the provider CLI; a failure is a <see cref="ProviderError" />.
/// </summary>
public interface IPromptRunner
{
	Provider Provider { get; }

	Task<Result<Unit, ProviderError>> Run(string model, ReasoningEffort effort, CancellationToken cancellationToken);
}

/// <summary>
///     The Claude CLI login: token expiry and refresh through the CLI, never by the service itself.
/// </summary>
public interface IClaudeSession
{
	/// <summary>Reads the expiry dates from the CLI credentials file.</summary>
	Task<Result<ClaudeTokenInfo, ProviderError>> ReadToken(CancellationToken cancellationToken);

	/// <summary>Runs a free CLI command that refreshes the token when it expires within five minutes.</summary>
	Task<Result<Unit, ProviderError>> RefreshThroughCli(CancellationToken cancellationToken);
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

	/// <summary>Schedules the reading that retries a failed automatic trigger, once its retry is due.</summary>
	string ScheduleTriggerRetry(Provider provider, DateTimeOffset runAt);

	string ScheduleKeepAlive(DateTimeOffset runAt);

	void EnqueueTrigger(string runId);

	/// <summary>Schedules the daily refresh of the model prices.</summary>
	void SchedulePriceRefresh();

	/// <summary>Schedules the daily purge of the job storage (failed jobs past their retention).</summary>
	void ScheduleJobPurge();

	void EnqueuePriceRefresh();

	void Delete(string jobId);
}

/// <summary>
///     The background job server, watched by the readiness probe.
/// </summary>
public interface IJobServerMonitor
{
	/// <summary>The latest heartbeat of a running job server, or <c>null</c> when none runs.</summary>
	Task<DateTimeOffset?> GetLastHeartbeat(CancellationToken cancellationToken);
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
