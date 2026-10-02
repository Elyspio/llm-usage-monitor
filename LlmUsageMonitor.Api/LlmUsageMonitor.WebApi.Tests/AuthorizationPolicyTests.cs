using System.Net;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class AuthorizationPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_endpoint_without_authorization_data_requires_a_signed_in_user()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/api/tests/unprotected", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task An_endpoint_without_authorization_data_requires_the_admin_role()
	{
		using var client = factory.CreateClientWithRoles();

		var response = await client.GetAsync("/api/tests/unprotected", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task An_endpoint_without_authorization_data_is_reached_by_the_admin()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/tests/unprotected", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Theory]
	[InlineData("/openapi/v1.json", "application/json")]
	[InlineData("/swagger/index.html", "text/html")]
	public async Task Outside_production_the_openapi_document_and_swagger_ui_are_public(string path, string mediaType)
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync(path, Token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
	}
}

public sealed class ProductionAuthorizationPolicyTests(ProductionApiFactory factory) : IClassFixture<ProductionApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("/openapi/v1.json")]
	[InlineData("/swagger/index.html")]
	[InlineData("/swagger/v1/swagger.json")]
	public async Task In_production_the_openapi_document_and_swagger_ui_are_not_served(string path)
	{
		using var anonymous = factory.CreateClient();
		using var admin = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var anonymousResponse = await anonymous.GetAsync(path, Token);
		var adminResponse = await admin.GetAsync(path, Token);

		anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		adminResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
	}

	[Theory]
	[InlineData("/", "text/html")]
	[InlineData("/settings", "text/html")]
	[InlineData(ApiFactory.AssetPath, "text/javascript")]
	[InlineData("/conf.js", "text/javascript")]
	[InlineData("/health/live", "text/plain")]
	public async Task In_production_the_spa_its_configuration_and_the_probes_stay_anonymous(string path, string mediaType)
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync(path, Token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
	}

	[Fact]
	public async Task In_production_the_api_still_requires_the_admin_role()
	{
		using var anonymous = factory.CreateClient();
		using var admin = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		(await anonymous.GetAsync("/api/dashboard", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await admin.GetAsync("/api/dashboard", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
	}
}
