using System.Diagnostics;
using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Core.Services;

public sealed class NotificationService(
	INotificationSender sender,
	ISettingsRepository settingsRepository,
	ISettingsService settingsService,
	ISecretProtector protector,
	IOptions<AppConfig> appConfig,
	TimeProvider time,
	ILogger<NotificationService> logger) : INotificationService
{
	public const string DeliveryFailedCode = "NOTIFICATION_FAILED";

	public async Task<bool> Notify(NotificationKind kind, Provider provider, string detail, CancellationToken cancellationToken)
	{
		var settings = (await settingsService.Get(cancellationToken)).Notifications;
		if (string.IsNullOrWhiteSpace(settings.Topic) || !settings.Events.For(provider).IsEnabled(kind))
		{
			return true;
		}

		try
		{
			await Deliver(Build(kind, provider, detail), settings, cancellationToken);
			return true;
		}
		// An HTTP timeout is an OperationCanceledException too: only the cancellation of the caller is let through.
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			// No queue: the failure is logged, traced and shown in the settings; the alerts are sent again by the next poll.
			logger.LogWarning(exception, "ntfy delivery failed for {Kind} ({Provider})", kind, provider);
			Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
			await SaveSendFailure(new(time.GetUtcNow(), exception.Message), cancellationToken);
			return false;
		}
	}

	public async Task SendTest(CancellationToken cancellationToken)
	{
		var settings = (await settingsService.Get(cancellationToken)).Notifications;
		if (string.IsNullOrWhiteSpace(settings.Topic))
		{
			throw new RequestValidationException(new Dictionary<string, string[]> { ["topic"] = ["Set a topic before sending a test."] });
		}

		var message = new NotificationMessage("LLM Usage Monitor : test", "Les notifications fonctionnent.", NotificationPriority.Default, ["test_tube"], appConfig.Value.PublicUrl);
		try
		{
			await Deliver(message, settings, cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			await SaveSendFailure(new(time.GetUtcNow(), exception.Message), cancellationToken);
			throw new ProviderException(DeliveryFailedCode, exception.Message, exception);
		}
	}

	private async Task Deliver(NotificationMessage message, NotificationSettings settings, CancellationToken cancellationToken)
	{
		var token = settings.ProtectedToken is { } protectedToken ? protector.Unprotect(protectedToken) : null;
		await sender.Send(message, settings.Url, settings.Topic!, token, cancellationToken);
		if (settings.LastSendFailure is { })
		{
			await SaveSendFailure(null, cancellationToken);
		}
	}

	private Task SaveSendFailure(NotificationSendFailure? failure, CancellationToken cancellationToken)
	{
		// Only its own field: a settings save running meanwhile is never undone.
		return settingsRepository.SaveSendFailure(failure, cancellationToken);
	}

	private NotificationMessage Build(NotificationKind kind, Provider provider, string detail)
	{
		var name = provider == Provider.Claude ? "Claude" : "Codex";
		var (title, priority, tag) = kind switch
		{
			NotificationKind.TriggerFailed => ($"{name} : déclenchement échoué", NotificationPriority.High, "x"),
			NotificationKind.AuthExpired => ($"{name} : connexion expirée", NotificationPriority.High, "key"),
			NotificationKind.ReadFailed => ($"{name} : lectures en échec", NotificationPriority.High, "warning"),
			NotificationKind.Reset => ($"{name} : reset détecté", NotificationPriority.Default, "arrows_counterclockwise"),
			NotificationKind.TriggerSucceeded => ($"{name} : nouveau cycle ouvert", NotificationPriority.Default, "white_check_mark"),
			_ => ($"{name} : rétabli", NotificationPriority.Default, "green_heart")
		};
		return new(title, detail, priority, [tag], appConfig.Value.PublicUrl);
	}
}

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
	private readonly IDataProtector _protector = provider.CreateProtector("llm-usage-monitor.settings.secrets");

	public string Protect(string value)
	{
		return _protector.Protect(value);
	}

	public string Unprotect(string value)
	{
		return _protector.Unprotect(value);
	}
}
