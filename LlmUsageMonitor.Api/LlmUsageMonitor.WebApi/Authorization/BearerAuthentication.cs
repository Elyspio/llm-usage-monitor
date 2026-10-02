using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LlmUsageMonitor.Authorization;

/// <summary>
///     Validation of the Keycloak access tokens sent to <c>/api</c>.
/// </summary>
public static class BearerAuthentication
{
	public static AuthenticationBuilder AddKeycloakBearer(this AuthenticationBuilder builder)
	{
		builder.AddJwtBearer();

		builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
			.Configure<IOptions<OidcConfig>, IHostEnvironment>((options, oidc, environment) =>
			{
				var config = oidc.Value;
				options.Authority = config.Issuer;
				// The development Keycloak started by Aspire may listen on HTTP only.
				options.RequireHttpsMetadata = !environment.IsDevelopment();
				options.TokenValidationParameters = new()
				{
					// Keycloak adds a client to the audience of any token carrying one of its roles (audience resolve mapper of the
					// "roles" scope): the audience would accept a token issued to any client of the realm. The authorized party
					// (azp, the client the token was issued to) is checked instead, below.
					ValidateAudience = false,
					ValidIssuer = config.Issuer,
					ClockSkew = TimeSpan.FromSeconds(30)
				};
				options.Events = new()
				{
					OnTokenValidated = context =>
					{
						var authorizedParty = (context.SecurityToken as JsonWebToken)?.Azp;
						if (!config.IsAuthorizedParty(authorizedParty))
						{
							context.Fail($"Access token issued to the client '{authorizedParty}', which is not an authorized party of the API.");
						}

						return Task.CompletedTask;
					}
				};
			});

		return builder;
	}
}
