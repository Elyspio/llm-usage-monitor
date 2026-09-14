using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LlmUsageMonitor.Controllers;

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/history")]
public sealed class HistoryController(IHistoryService history) : ControllerBase
{
	/// <summary>Snapshots per window and triggers of the last 24 hours (<c>24h</c>) or 7 days (<c>7d</c>).</summary>
	[HttpGet(Name = "GetHistory")]
	public Task<UsageHistory> Get([FromQuery] Provider? provider, [FromQuery] string? windowId, [FromQuery] string range, CancellationToken cancellationToken)
	{
		var span = range switch
		{
			"24h" => TimeSpan.FromHours(24),
			"7d" => TimeSpan.FromDays(7),
			_ => throw new RequestValidationException(new Dictionary<string, string[]> { ["range"] = ["Valeurs possibles : 24h, 7d."] }),
		};
		return history.Get(provider, windowId, span, cancellationToken);
	}
}

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/providers")]
public sealed class ProvidersController(ITriggerService triggers) : ControllerBase
{
	/// <summary>Queues a manual trigger; 409 when a CLI process already runs for the provider.</summary>
	[HttpPost("{provider}/trigger", Name = "TriggerProvider")]
	[ProducesResponseType<TriggerRun>(StatusCodes.Status202Accepted)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public async Task<IActionResult> Trigger(Provider provider, CancellationToken cancellationToken)
	{
		var run = await triggers.RequestManual(provider, cancellationToken);
		return AcceptedAtRoute("GetTriggerRun", new { id = run.Id }, run);
	}
}

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/trigger-runs")]
public sealed class TriggerRunsController(ITriggerService triggers) : ControllerBase
{
	[HttpGet("{id}", Name = "GetTriggerRun")]
	[ProducesResponseType<TriggerRun>(StatusCodes.Status200OK)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
	public Task<TriggerRun> Get(string id, CancellationToken cancellationToken) => triggers.Get(id, cancellationToken);
}

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settings, INotificationService notifications) : ControllerBase
{
	[HttpGet("polling", Name = "GetPollingSettings")]
	public async Task<PollingSettings> GetPolling(CancellationToken cancellationToken) => (await settings.Get(cancellationToken)).Polling;

	[HttpPut("polling", Name = "UpdatePollingSettings")]
	[ProducesResponseType<PollingSettings>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<PollingSettings> UpdatePolling(PollingSettings polling, CancellationToken cancellationToken) => settings.UpdatePolling(polling, cancellationToken);

	[HttpGet("triggers", Name = "GetTriggerSettings")]
	public async Task<TriggerSettings> GetTriggers(CancellationToken cancellationToken) => (await settings.Get(cancellationToken)).Triggers;

	[HttpPut("triggers", Name = "UpdateTriggerSettings")]
	[ProducesResponseType<TriggerSettings>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<TriggerSettings> UpdateTriggers(TriggerSettings triggers, CancellationToken cancellationToken) => settings.UpdateTriggers(triggers, cancellationToken);

	[HttpGet("notifications", Name = "GetNotificationSettings")]
	public Task<NotificationSettingsView> GetNotifications(CancellationToken cancellationToken) => settings.GetNotifications(cancellationToken);

	[HttpPut("notifications", Name = "UpdateNotificationSettings")]
	[ProducesResponseType<NotificationSettingsView>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<NotificationSettingsView> UpdateNotifications(NotificationSettingsUpdate update, CancellationToken cancellationToken) =>
		settings.UpdateNotifications(update, cancellationToken);

	[HttpPost("notifications/test", Name = "SendTestNotification")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
	public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
	{
		await notifications.SendTest(cancellationToken);
		return NoContent();
	}
}
