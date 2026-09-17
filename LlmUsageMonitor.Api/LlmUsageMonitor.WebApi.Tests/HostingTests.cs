using System.Net;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class HostingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	[Fact]
	public async Task The_runtime_configuration_is_public_and_carries_the_configured_realm()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/conf.js", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
		var script = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		script.ShouldContain($"authority: \"{ApiFactory.Issuer}\"");
		script.ShouldContain($"clientId: \"{ApiFactory.ClientId}\"");
		script.ShouldContain("window.location.origin");
	}

	[Fact]
	public async Task A_published_asset_is_served_as_itself_and_not_as_the_single_page_shell()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync(ApiFactory.AssetPath, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
		(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("marker");
	}

	[Theory]
	[InlineData("/")]
	[InlineData("/settings")]
	public async Task A_client_route_falls_back_to_the_single_page_shell(string path)
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
		(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(ApiFactory.IndexMarker);
	}

	[Fact]
	public async Task An_unknown_api_route_stays_a_404_instead_of_the_shell()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/unknown", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}
}