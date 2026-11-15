using Elyspio.Utils.Results.AspNetCore;
using Elyspio.Utils.Results;
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
	[ProducesResponseType<UsageHistory>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<UsageHistory>> Get([FromQuery] Provider? provider, [FromQuery] string? windowId, [FromQuery] string range, CancellationToken cancellationToken)
	{
		TimeSpan? span = range switch
		{
			"24h" => TimeSpan.FromHours(24),
			"7d" => TimeSpan.FromDays(7),
			_ => null
		};
		if (span is not { } value) return new RequestInvalid("range", "Possible values: 24h, 7d.").ToActionResult();

		return await history.Get(provider, windowId, value, cancellationToken);
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
		var requested = await triggers.RequestManual(provider, cancellationToken);
		return requested.TryGet(out var run, out var error) ? AcceptedAtRoute("GetTriggerRun", new { id = run.Id }, run) : error.ToActionResult();
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
	public async Task<ActionResult<TriggerRun>> Get(string id, CancellationToken cancellationToken)
	{
		return (await triggers.Get(id, cancellationToken)).ToActionResult();
	}
}

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settings, INotificationService notifications) : ControllerBase
{
	[HttpGet("polling", Name = "GetPollingSettings")]
	public async Task<PollingSettings> GetPolling(CancellationToken cancellationToken)
	{
		return (await settings.Get(cancellationToken)).Polling;
	}

	[HttpPut("polling", Name = "UpdatePollingSettings")]
	[ProducesResponseType<PollingSettings>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<PollingSettings>> UpdatePolling(PollingSettings polling, CancellationToken cancellationToken)
	{
		return (await settings.UpdatePolling(polling, cancellationToken)).ToActionResult();
	}

	[HttpGet("triggers", Name = "GetTriggerSettings")]
	public async Task<TriggerSettings> GetTriggers(CancellationToken cancellationToken)
	{
		return (await settings.Get(cancellationToken)).Triggers;
	}

	[HttpPut("triggers", Name = "UpdateTriggerSettings")]
	[ProducesResponseType<TriggerSettings>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<TriggerSettings>> UpdateTriggers(TriggerSettings triggers, CancellationToken cancellationToken)
	{
		return (await settings.UpdateTriggers(triggers, cancellationToken)).ToActionResult();
	}

	[HttpGet("notifications", Name = "GetNotificationSettings")]
	public Task<NotificationSettingsView> GetNotifications(CancellationToken cancellationToken)
	{
		return settings.GetNotifications(cancellationToken);
	}

	[HttpPut("notifications", Name = "UpdateNotificationSettings")]
	[ProducesResponseType<NotificationSettingsView>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public async Task<ActionResult<NotificationSettingsView>> UpdateNotifications(NotificationSettingsUpdate update, CancellationToken cancellationToken)
	{
		return (await settings.UpdateNotifications(update, cancellationToken)).ToActionResult();
	}

	[HttpPost("notifications/test", Name = "SendTestNotification")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
	public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
	{
		return (await notifications.SendTest(cancellationToken)).ToActionResult();
	}
}