using System.Net;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class DashboardEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	[Fact]
	public async Task Anonymous_request_is_rejected_with_401()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/dashboard", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Token_without_the_admin_role_is_rejected_with_403()
	{
		using var client = factory.CreateClientWithRoles();

		var response = await client.GetAsync("/api/dashboard", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Admin_gets_every_provider_as_camel_case_strings()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/dashboard", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		body.RootElement.GetProperty("providers")
			.EnumerateArray()
			.Select(provider => provider.GetProperty("provider").GetString())
			.ShouldBe(["claude", "codex"]);
	}
}
