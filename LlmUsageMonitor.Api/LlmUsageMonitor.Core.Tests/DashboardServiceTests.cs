using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class DashboardServiceTests
{
	[Fact]
	public async Task GetDashboard_lists_every_provider()
	{
		var dashboard = await new DashboardService().GetDashboard(TestContext.Current.CancellationToken);

		dashboard.Providers.Select(provider => provider.Provider).ShouldBe([Provider.Claude, Provider.Codex]);
	}
}
