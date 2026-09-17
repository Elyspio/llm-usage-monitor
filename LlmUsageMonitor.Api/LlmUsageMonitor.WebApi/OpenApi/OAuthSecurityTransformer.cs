using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace LlmUsageMonitor.OpenApi;

/// <summary>
///     Declares the Keycloak authorization code flow, so Swagger UI can obtain a bearer token with PKCE.
/// </summary>
public sealed class OAuthSecurityTransformer(IOptions<OidcConfig> oidc) : IOpenApiDocumentTransformer
{
	private const string SchemeId = "keycloak";

	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
	{
		var authority = oidc.Value.Authority.TrimEnd('/');

		document.Components ??= new();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
		document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
		{
			Type = SecuritySchemeType.OAuth2,
			Flows = new()
			{
				AuthorizationCode = new()
				{
					AuthorizationUrl = new($"{authority}/protocol/openid-connect/auth"),
					TokenUrl = new($"{authority}/protocol/openid-connect/token"),
					Scopes = new Dictionary<string, string> { ["openid"] = "OpenID Connect" }
				}
			}
		};
		document.Security = [new() { [new(SchemeId, document)] = ["openid"] }];

		return Task.CompletedTask;
	}
}