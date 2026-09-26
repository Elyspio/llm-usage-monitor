using System.Text.RegularExpressions;
using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     Behaviour settings: validated on save and applied at once (poll jobs rewritten, other settings read by every job).
/// </summary>
public sealed partial class SettingsService(
	ISettingsRepository repository,
	IProviderStateRepository states,
	IJobScheduler scheduler,
	ISecretProtector protector,
	IOptions<AppConfig> appConfig) : ISettingsService
{
	public const int MaxModelLength = 100;
	public const string IntervalDivisorMessage = "A divisor of 60 is expected: 1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30 or 60 minutes.";

	public async Task<AppSettings> Get(CancellationToken cancellationToken)
	{
		if (await repository.Find(cancellationToken) is { } settings)
		{
			return settings;
		}

		var defaults = AppSettings.CreateDefault(appConfig.Value.AutoTriggerEnabledByDefault);
		await repository.Save(defaults, cancellationToken);
		return defaults;
	}

	public async Task<PollingSettings> UpdatePolling(PollingSettings polling, CancellationToken cancellationToken)
	{
		var errors = new Dictionary<string, string[]>();
		ValidateInterval(errors, "claudeIntervalMinutes", polling.ClaudeIntervalMinutes);
		ValidateInterval(errors, "codexIntervalMinutes", polling.CodexIntervalMinutes);
		ThrowIfAny(errors);

		var settings = await Get(cancellationToken);
		await repository.Save(settings with { Polling = polling }, cancellationToken);
		foreach (var provider in Enum.GetValues<Provider>()) scheduler.SetPollInterval(provider, polling.For(provider));
		return polling;
	}

	public async Task<TriggerSettings> UpdateTriggers(TriggerSettings triggers, CancellationToken cancellationToken)
	{
		var errors = new Dictionary<string, string[]>();
		ValidateModel(errors, "claude.model", triggers.Claude.Model);
		ValidateModel(errors, "codex.model", triggers.Codex.Model);
		ThrowIfAny(errors);

		var normalized = new TriggerSettings(triggers.Claude with { Model = triggers.Claude.Model.Trim() }, triggers.Codex with { Model = triggers.Codex.Model.Trim() });
		var settings = await Get(cancellationToken);
		await repository.Save(settings with { Triggers = normalized }, cancellationToken);

		foreach (var provider in Enum.GetValues<Provider>().Where(provider => !normalized.For(provider).AutoEnabled)) await CancelPendingResetCheck(provider, cancellationToken);

		return normalized;
	}

	public async Task<NotificationSettingsView> GetNotifications(CancellationToken cancellationToken)
	{
		return ToView((await Get(cancellationToken)).Notifications);
	}

	public async Task<NotificationSettingsView> UpdateNotifications(NotificationSettingsUpdate update, CancellationToken cancellationToken)
	{
		var errors = new Dictionary<string, string[]>();
		if (!Uri.TryCreate(update.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
		{
			errors["url"] = ["Absolute http(s) URL expected."];
		}

		var topic = string.IsNullOrWhiteSpace(update.Topic) ? null : update.Topic.Trim();
		if (topic is { } && !TopicPattern().IsMatch(topic))
		{
			errors["topic"] = ["Letters, digits, _ and - only, 64 characters at most."];
		}

		if (update.ReadFailureThreshold is < NotificationSettings.MinReadFailureThreshold or > NotificationSettings.MaxReadFailureThreshold)
		{
			errors["readFailureThreshold"] = [$"Between {NotificationSettings.MinReadFailureThreshold} and {NotificationSettings.MaxReadFailureThreshold}."];
		}

		ThrowIfAny(errors);

		var settings = await Get(cancellationToken);
		var protectedToken = update.Token switch
		{
			null => settings.Notifications.ProtectedToken,
			"" => null,
			var token => protector.Protect(token)
		};
		var notifications = settings.Notifications with
		{
			Url = update.Url.Trim(),
			Topic = topic,
			ProtectedToken = protectedToken,
			Events = update.Events,
			ReadFailureThreshold = update.ReadFailureThreshold
		};
		await repository.Save(settings with { Notifications = notifications }, cancellationToken);
		return ToView(notifications);
	}

	private async Task CancelPendingResetCheck(Provider provider, CancellationToken cancellationToken)
	{
		var state = await states.Get(provider, cancellationToken);
		if (state.PendingResetCheck is not { } pending)
		{
			return;
		}

		scheduler.Delete(pending.JobId);
		await states.Save(state with { PendingResetCheck = null }, cancellationToken);
	}

	private static NotificationSettingsView ToView(NotificationSettings settings)
	{
		return new(settings.Url, settings.Topic, settings.ProtectedToken is not null, settings.Events, settings.ReadFailureThreshold, settings.LastSendFailure);
	}

	private static void ValidateInterval(Dictionary<string, string[]> errors, string field, int minutes)
	{
		if (minutes is < PollingSettings.MinIntervalMinutes or > PollingSettings.MaxIntervalMinutes)
		{
			errors[field] = [$"Between {PollingSettings.MinIntervalMinutes} and {PollingSettings.MaxIntervalMinutes} minutes."];
		}
		else if (!PollingSettings.DividesHour(minutes))
		{
			errors[field] = [IntervalDivisorMessage];
		}
	}

	private static void ValidateModel(Dictionary<string, string[]> errors, string field, string? model)
	{
		if (string.IsNullOrWhiteSpace(model) || model.Trim().Length > MaxModelLength)
		{
			errors[field] = [$"Model required, {MaxModelLength} characters at most."];
		}
	}

	private static void ThrowIfAny(Dictionary<string, string[]> errors)
	{
		if (errors.Count > 0)
		{
			throw new RequestValidationException(errors);
		}
	}

	[GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
	private static partial Regex TopicPattern();
}