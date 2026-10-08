using System.Net.Http.Json;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class HistoryEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_history_holds_the_readings_and_triggers_of_the_provider_over_the_range()
	{
		var now = DateTimeOffset.UtcNow;
		var snapshots = factory.Services.GetRequiredService<IUsageSnapshotRepository>();
		var runs = factory.Services.GetRequiredService<ITriggerRunRepository>();
		await snapshots.Add(Provider.Claude, new(now.AddHours(-2), [new("five_hour", 20, now.AddHours(1), 300), new("seven_day", 50, now.AddDays(3), 10080)]), Token);
		await snapshots.Add(Provider.Claude, new(now.AddHours(-1), [new("five_hour", 35, now.AddHours(1), 300), new("seven_day", 55, now.AddDays(3), 10080)]), Token);
		await snapshots.Add(Provider.Codex, new(now.AddHours(-1), [new("codex/primary", 10, now.AddDays(2), 10080)]), Token);
		var recent = await runs.StartManual(Provider.Claude, new(true, "claude-sonnet", ReasoningEffort.None), now.AddMinutes(-30), Token);
		await runs.StartManual(Provider.Claude, new(true, "claude-sonnet", ReasoningEffort.None), now.AddDays(-2), Token);
		await runs.StartManual(Provider.Codex, new(true, "gpt", ReasoningEffort.None), now.AddMinutes(-20), Token);
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var history = await client.GetFromJsonAsync<JsonElement>("/api/history?provider=claude&range=24h", Token);

		var series = history.GetProperty("series").EnumerateArray().ToList();
		series.Select(item => item.GetProperty("provider").GetString()).Distinct().ShouldBe(["claude"]);
		var session = series.Single(item => item.GetProperty("windowId").GetString() == "five_hour").GetProperty("points").EnumerateArray().ToList();
		session.Select(point => point.GetProperty("usedPercent").GetDouble()).ShouldBe([20, 35]);
		series.Single(item => item.GetProperty("windowId").GetString() == "seven_day").GetProperty("points").GetArrayLength().ShouldBe(2);
		// The triggers of the provider within the range only: the one of two days ago and the Codex one are left out.
		history.GetProperty("triggerRuns").EnumerateArray().Select(run => run.GetProperty("id").GetString()).ShouldBe([recent.Id]);
	}

	[Fact]
	public async Task A_window_can_be_read_alone()
	{
		var now = DateTimeOffset.UtcNow;
		await factory.Services.GetRequiredService<IUsageSnapshotRepository>()
			.Add(Provider.Codex, new(now.AddMinutes(-10), [new("codex/primary", 10, now.AddDays(2), 10080), new("codex/secondary", 70, now.AddDays(5), 10080)]), Token);
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var history = await client.GetFromJsonAsync<JsonElement>("/api/history?provider=codex&windowId=codex/secondary&range=7d", Token);

		var series = history.GetProperty("series").EnumerateArray().ShouldHaveSingleItem();
		series.GetProperty("windowId").GetString().ShouldBe("codex/secondary");
		series.GetProperty("points")[0].GetProperty("usedPercent").GetDouble().ShouldBe(70);
	}
}
