using System.Net;
using LlmUsageMonitor.Hosting;
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
		script.ShouldContain("version: \"0.0.0-dev\"");
		response.Headers.CacheControl!.NoStore.ShouldBeTrue();
	}

	[Fact]
	public async Task Outside_production_no_content_security_policy_is_sent()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

		response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
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
		// Signed in: an anonymous request is turned away by the fallback policy before any route is looked up.
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/unknown", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}
}

public sealed class EmbeddedSpaTests
{
	[Fact]
	public void A_release_build_serves_the_spa_embedded_in_the_executable()
	{
		var spa = ProductionHosting.EmbeddedSpa(typeof(EmbeddedSpaTests).Assembly).ShouldNotBeNull();

		spa.GetFileInfo("index.html").Exists.ShouldBeTrue();
		spa.GetFileInfo("assets/index-embedded.js").Exists.ShouldBeTrue();
	}

	[Fact]
	public void Another_build_has_no_embedded_spa()
	{
		ProductionHosting.EmbeddedSpa(typeof(ProductionHosting).Assembly).ShouldBeNull();
	}
}

public sealed class ProductionHostingTests(ProductionApiFactory factory) : IClassFixture<ProductionApiFactory>
{
	[Theory]
	[InlineData("/")]
	[InlineData("/settings")]
	[InlineData(ApiFactory.AssetPath)]
	[InlineData("/conf.js")]
	[InlineData("/api/dashboard")]
	public async Task In_production_every_response_carries_a_strict_content_security_policy(string path)
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

		var directives = response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().Split("; ");
		directives.ShouldContain("default-src 'self'");
		directives.ShouldContain("script-src 'self'");
		directives.ShouldContain("connect-src 'self' https://auth.test");
		directives.ShouldContain("object-src 'none'");
		directives.ShouldContain("frame-ancestors 'none'");
		directives.ShouldAllBe(directive => directive.StartsWith("style-src") || !directive.Contains("'unsafe-inline'"));
		directives.ShouldAllBe(directive => !directive.Contains("'unsafe-eval'"));
	}

	[Fact]
	public async Task The_hangfire_dashboard_keeps_its_own_pages_without_the_policy()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/hangfire", TestContext.Current.CancellationToken);

		response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
	}
}
