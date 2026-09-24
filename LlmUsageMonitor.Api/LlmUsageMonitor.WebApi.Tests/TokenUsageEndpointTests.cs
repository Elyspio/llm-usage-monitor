using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class TokenUsageEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_upload_without_the_admin_role_is_rejected_with_403()
	{
		using var client = factory.CreateClientWithRoles();

		var response = await client.PostAsJsonAsync("/api/token-usage", Upload("pc-denied", DateTimeOffset.UtcNow), Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task An_uploaded_hour_is_in_the_24_hour_report_of_its_workstation()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);
		var now = DateTimeOffset.UtcNow;
		var hour = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);

		var uploaded = await client.PostAsJsonAsync("/api/token-usage", Upload("pc-report", hour), Token);
		var report = await client.GetAsync("/api/token-usage?range=24h&machineId=pc-report&timeZone=Europe/Paris", Token);

		uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Token));
		(await uploaded.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("stored").GetInt32().ShouldBe(1);
		report.StatusCode.ShouldBe(HttpStatusCode.OK, await report.Content.ReadAsStringAsync(Token));
		var body = await report.Content.ReadFromJsonAsync<JsonElement>(Token);
		body.GetProperty("step").GetString().ShouldBe("hour");
		var row = body.GetProperty("rows").EnumerateArray().ShouldHaveSingleItem();
		row.GetProperty("provider").GetString().ShouldBe("codex");
		row.GetProperty("tokens").GetProperty("cacheRead").GetInt64().ShouldBe(800);
		body.GetProperty("machines").EnumerateArray().Select(machine => machine.GetProperty("name").GetString()).ShouldContain("Laptop");
	}

	[Fact]
	public async Task Only_the_known_ranges_are_accepted()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/token-usage?range=1y", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	private static object Upload(string machineId, DateTimeOffset hour)
	{
		return new
		{
			machineId,
			machineName = "Laptop",
			buckets = new[]
			{
				new { provider = "codex", model = "gpt-6-sol", hour = new DateTimeOffset(hour.UtcTicks - hour.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero), tokens = new { input = 100, cacheRead = 800, cacheWrite = 0, output = 20 } }
			}
		};
	}
}
