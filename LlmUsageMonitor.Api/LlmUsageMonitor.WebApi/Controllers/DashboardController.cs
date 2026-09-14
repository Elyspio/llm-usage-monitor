using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LlmUsageMonitor.Controllers;

[ApiController]
[Authorize(Policy = AdminPolicy.Name)]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
	[HttpGet(Name = "GetDashboard")]
	public Task<DashboardSnapshot> Get(CancellationToken cancellationToken) => dashboard.GetDashboard(cancellationToken);
}
