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

	public async Task Notify(NotificationKind kind, Provider provider, string detail, CancellationToken cancellationToken)
	{
		var settings = (await settingsService.Get(cancellationToken)).Notifications;
		if (string.IsNullOrWhiteSpace(settings.Topic) || !settings.Events.IsEnabled(kind))
		{
			return;
		}

		try
		{
			await Deliver(Build(kind, provider, detail), settings, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Neither retry nor queue: the failure is logged, traced and shown in the settings.
			logger.LogWarning(exception, "ntfy delivery failed for {Kind} ({Provider})", kind, provider);
			Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
			await RecordFailure(exception.Message, cancellationToken);
		}
	}

	public async Task SendTest(CancellationToken cancellationToken)
	{
		var settings = (await settingsService.Get(cancellationToken)).Notifications;
		if (string.IsNullOrWhiteSpace(settings.Topic))
		{
			throw new RequestValidationException(new Dictionary<string, string[]> { ["topic"] = ["Renseigner un topic avant d'envoyer un test."] });
		}

		var message = new NotificationMessage("LLM Usage Monitor : test", "Les notifications fonctionnent.", NotificationPriority.Default, ["test_tube"], appConfig.Value.PublicUrl);
		try
		{
			await Deliver(message, settings, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			await RecordFailure(exception.Message, cancellationToken);
			throw new ProviderException(DeliveryFailedCode, exception.Message, exception);
		}
	}

	private Task Deliver(NotificationMessage message, NotificationSettings settings, CancellationToken cancellationToken)
	{
		var token = settings.ProtectedToken is { } protectedToken ? protector.Unprotect(protectedToken) : null;
		return sender.Send(message, settings.Url, settings.Topic!, token, cancellationToken);
	}

	private async Task RecordFailure(string message, CancellationToken cancellationToken)
	{
		var settings = await settingsService.Get(cancellationToken);
		var failure = new NotificationSendFailure(time.GetUtcNow(), message);
		await settingsRepository.Save(settings with { Notifications = settings.Notifications with { LastSendFailure = failure } }, cancellationToken);
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