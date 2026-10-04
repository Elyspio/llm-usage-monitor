using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LlmUsageMonitor.Controllers;

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
public sealed class ResetCreditsController(IResetCreditService credits, ISettingsService settings) : ControllerBase
{
	/// <summary>Consumes the selected earned reset. The same UUID replays the same logical request.</summary>
	[HttpPost("api/providers/{provider}/reset-credits/consume", Name = "ConsumeResetCredit")]
	[ProducesResponseType<ResetCreditRun>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
	public Task<ResetCreditRun> Consume(Provider provider, ConsumeResetCreditRequest request, CancellationToken cancellationToken)
		=> credits.ConsumeManual(provider, request, cancellationToken);

	[HttpGet("api/settings/reset-credits", Name = "GetResetCreditSettings")]
	public async Task<ResetCreditSettings> Get(CancellationToken cancellationToken) => (await settings.Get(cancellationToken)).ResetCredits;

	[HttpPut("api/settings/reset-credits", Name = "UpdateResetCreditSettings")]
	[ProducesResponseType<ResetCreditSettings>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<ResetCreditSettings> Update(ResetCreditSettings update, CancellationToken cancellationToken) => settings.UpdateResetCredits(update, cancellationToken);
}
