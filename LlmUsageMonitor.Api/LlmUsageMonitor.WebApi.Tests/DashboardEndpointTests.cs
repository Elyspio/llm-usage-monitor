using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class DashboardEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Anonymous_request_is_rejected_with_401()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/dashboard", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Token_without_the_admin_role_is_rejected_with_403()
	{
		using var client = factory.CreateClientWithRoles();

		var response = await client.GetAsync("/api/dashboard", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_gets_every_provider_as_camel_case_strings()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var body = await GetJson(client, "/api/dashboard");

		body.GetProperty("providers").EnumerateArray().Select(provider => provider.GetProperty("provider").GetString()).ShouldBe(["claude", "codex"]);
		body.GetProperty("providers")[0].GetProperty("pollIntervalMinutes").GetInt32().ShouldBe(3);
	}

	[Fact]
	public async Task A_manual_trigger_is_accepted_then_refused_while_it_runs()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var accepted = await client.PostAsync("/api/providers/claude/trigger", null, Token);
		var busy = await client.PostAsync("/api/providers/claude/trigger", null, Token);

		accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
		var run = await accepted.Content.ReadFromJsonAsync<JsonElement>(Token);
		run.GetProperty("status").GetString().ShouldBe("running");
		run.GetProperty("manual").GetBoolean().ShouldBeTrue();
		factory.Scheduler.EnqueuedTriggers.ShouldContain(run.GetProperty("id").GetString()!);
		accepted.Headers.Location!.ToString().ShouldEndWith($"/api/trigger-runs/{run.GetProperty("id").GetString()}");

		busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
		(await busy.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("code").GetString().ShouldBe("CLI_BUSY");

		var fetched = await GetJson(client, $"/api/trigger-runs/{run.GetProperty("id").GetString()}");
		fetched.GetProperty("provider").GetString().ShouldBe("claude");
	}

	[Fact]
	public async Task An_unknown_trigger_run_is_404()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/trigger-runs/000000000000000000000000", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Invalid_settings_are_rejected_with_one_error_per_field()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.PutAsJsonAsync("/api/settings/polling", new { claudeIntervalMinutes = 0, codexIntervalMinutes = 3 }, Token);

		response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
		problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["claudeIntervalMinutes"]);
	}

	[Fact]
	public async Task The_ntfy_token_is_never_sent_back()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);
		var update = new
		{
			url = "https://ntfy.sh",
			topic = "llm_usage_monitor_tests",
			token = "tk_secret_value",
			events = new { triggerFailed = true, authExpired = true, readFailed = true, reset = false, triggerSucceeded = true, recovered = true },
			readFailureThreshold = 3,
		};

		var saved = await client.PutAsJsonAsync("/api/settings/notifications", update, Token);
		var read = await client.GetStringAsync("/api/settings/notifications", Token);

		saved.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await saved.Content.ReadAsStringAsync(Token)).ShouldNotContain("tk_secret_value");
		read.ShouldNotContain("tk_secret_value");
		JsonDocument.Parse(read).RootElement.GetProperty("tokenDefined").GetBoolean().ShouldBeTrue();
	}

	[Fact]
	public async Task History_accepts_24h_and_7d_only()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var day = await client.GetAsync("/api/history?range=24h", Token);
		var invalid = await client.GetAsync("/api/history?range=1y", Token);

		day.StatusCode.ShouldBe(HttpStatusCode.OK);
		invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	private static async Task<JsonElement> GetJson(HttpClient client, string url)
	{
		var response = await client.GetAsync(url, Token);
		response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
		return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
	}
}
