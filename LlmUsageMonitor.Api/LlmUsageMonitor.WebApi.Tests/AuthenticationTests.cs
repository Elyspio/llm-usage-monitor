using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(ApiFactory.ClientId)]
	[InlineData("i-llm-usage-collector")]
	[InlineData("i-elytools")]
	public async Task A_token_of_an_authorized_client_is_accepted(string authorizedParty)
	{
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor => descriptor.Claims["azp"] = authorizedParty);

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Fact]
	public async Task A_token_issued_to_another_client_of_the_realm_is_rejected()
	{
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor => descriptor.Claims["azp"] = "i-other-app");

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task A_token_without_authorized_party_is_rejected()
	{
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor => descriptor.Claims.Remove("azp"));

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task A_token_of_another_issuer_is_rejected()
	{
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor => descriptor.Issuer = "https://auth.test/realms/other");

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task An_expired_token_is_rejected()
	{
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor =>
		{
			descriptor.IssuedAt = DateTime.UtcNow.AddMinutes(-10);
			descriptor.NotBefore = DateTime.UtcNow.AddMinutes(-10);
			descriptor.Expires = DateTime.UtcNow.AddMinutes(-2);
		});

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task A_token_signed_with_another_key_is_rejected()
	{
		var otherKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test" };
		var token = ApiFactory.CreateToken([ApiFactory.AdminRole], descriptor => descriptor.SigningCredentials = new(otherKey, SecurityAlgorithms.RsaSha256));

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
	}

	[Theory]
	[InlineData("not an object")]
	[InlineData("client entry not an object")]
	[InlineData("roles not an array")]
	[InlineData("roles not strings")]
	public async Task A_malformed_role_claim_is_a_403(string shape)
	{
		object resourceAccess = shape switch
		{
			"not an object" => ApiFactory.AdminRole,
			"client entry not an object" => new Dictionary<string, object> { [ApiFactory.ClientId] = ApiFactory.AdminRole },
			"roles not an array" => new Dictionary<string, object> { [ApiFactory.ClientId] = new Dictionary<string, object> { ["roles"] = ApiFactory.AdminRole } },
			_ => new Dictionary<string, object> { [ApiFactory.ClientId] = new Dictionary<string, object> { ["roles"] = new object[] { 1, new Dictionary<string, object> { ["name"] = "x" }, true } } }
		};
		var token = ApiFactory.CreateToken([], descriptor => descriptor.Claims["resource_access"] = resourceAccess);

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task The_admin_role_is_found_among_values_of_other_types()
	{
		var resourceAccess = new Dictionary<string, object> { [ApiFactory.ClientId] = new Dictionary<string, object> { ["roles"] = new object[] { 1, ApiFactory.AdminRole } } };
		var token = ApiFactory.CreateToken([], descriptor => descriptor.Claims["resource_access"] = resourceAccess);

		var response = await Get(token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Fact]
	public async Task An_authority_configured_with_a_trailing_slash_still_accepts_the_tokens()
	{
		await using var slashed = factory.WithWebHostBuilder(builder =>
		{
			builder.UseSetting("Oidc:Authority", $"{ApiFactory.Issuer}/");
			// The issuer of the metadata is accepted too: an unrelated one leaves the configured authority alone in question.
			builder.ConfigureTestServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
				options => options.Configuration = new() { Issuer = "https://metadata.test/realms/unrelated" }));
		});
		using var client = slashed.CreateClient();
		client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.CreateToken([ApiFactory.AdminRole]));

		var response = await client.GetAsync("/api/dashboard", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	private async Task<HttpResponseMessage> Get(string accessToken)
	{
		using var client = factory.CreateClientWithToken(accessToken);
		return await client.GetAsync("/api/dashboard", Token);
	}
}
