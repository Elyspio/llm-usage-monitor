using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LlmUsageMonitor.WebApi.Tests;

/// <summary>
///     Hosts the API with a local signing key in place of Keycloak, so tests issue their own access tokens.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
	public const string Issuer = "https://auth.test/realms/llm-usage-monitor";
	public const string ClientId = "i-llm-usage-monitor";
	public const string AdminRole = "llm-usage-monitor:admin";

	private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Testing");
		builder.UseSetting("Oidc:Authority", Issuer);
		builder.UseSetting("Oidc:ClientId", ClientId);
		builder.ConfigureTestServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
		{
			// Set before the JwtBearer post-configuration, a static configuration keeps the handler from fetching Keycloak metadata.
			options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
			options.TokenValidationParameters.IssuerSigningKey = SigningKey;
		}));
	}

	/// <summary>
	///     Creates a client authenticated with an access token carrying the given client roles.
	/// </summary>
	public HttpClient CreateClientWithRoles(params string[] clientRoles)
	{
		var client = CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(clientRoles));
		return client;
	}

	private static string CreateToken(string[] clientRoles) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
	{
		Issuer = Issuer,
		Audience = ClientId,
		Expires = DateTime.UtcNow.AddMinutes(5),
		Claims = new Dictionary<string, object>
		{
			["sub"] = "test-user",
			["resource_access"] = new Dictionary<string, object> { [ClientId] = new Dictionary<string, object> { ["roles"] = clientRoles } },
		},
		SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
	});
}
