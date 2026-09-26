using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LlmUsageMonitor.Controllers;

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/token-usage")]
public sealed class TokenUsageController(ITokenUsageService tokenUsage) : ControllerBase
{
	/// <summary>
	///     Tokens and cost of the last 24 hours (<c>24h</c>, by hour), of the last 7, 30 or 90 days (<c>7d</c>, <c>30d</c>,
	///     <c>90d</c>) or since the first upload (<c>all</c>), by day of <paramref name="timeZone" />, for every workstation or one.
	/// </summary>
	[HttpGet(Name = "GetTokenUsage")]
	[ProducesResponseType<TokenUsageReport>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<TokenUsageReport> Get([FromQuery] string range, [FromQuery] string? machineId, [FromQuery] string? timeZone, CancellationToken cancellationToken)
	{
		var period = range switch
		{
			"24h" => TokenUsageRange.Last24Hours,
			"7d" => TokenUsageRange.Last7Days,
			"30d" => TokenUsageRange.Last30Days,
			"90d" => TokenUsageRange.Last90Days,
			"all" => TokenUsageRange.All,
			_ => throw new RequestValidationException(new Dictionary<string, string[]> { ["range"] = ["Valeurs possibles : 24h, 7d, 30d, 90d, all."] })
		};
		return tokenUsage.Get(period, machineId, timeZone, cancellationToken);
	}

	/// <summary>
	///     Stores the hourly totals of a workstation; each bucket replaces the stored one, so a repeated upload changes nothing.
	/// </summary>
	[HttpPost(Name = "UploadTokenUsage")]
	[ProducesResponseType<TokenUsageUploadResult>(StatusCodes.Status200OK)]
	[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
	public Task<TokenUsageUploadResult> Upload(TokenUsageUpload upload, CancellationToken cancellationToken)
	{
		return tokenUsage.Upload(upload, cancellationToken);
	}
}
