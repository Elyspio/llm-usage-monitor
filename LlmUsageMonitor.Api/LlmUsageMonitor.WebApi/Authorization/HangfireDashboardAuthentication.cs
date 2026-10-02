using System.Security.Claims;
using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LlmUsageMonitor.Authorization;

/// <summary>
///     Sign-in of the Hangfire dashboard: a cookie scheme reserved to <c>/hangfire</c>, challenged with the public client in
///     code flow + PKCE (no secret) on <c>/signin-oidc</c>. The dashboard actions (POST) carry an antiforgery token, checked by
///     Hangfire as soon as <see cref="IAntiforgery" /> is registered.
/// </summary>
public static class HangfireDashboardAuthentication
{
	public const string CookieScheme = "HangfireCookie";
	public const string OidcScheme = "HangfireOidc";
	public const string PolicyName = "HangfireDashboard";
	public const string CookieName = "llm-usage-monitor.hangfire";
	public const string AntiforgeryCookieName = "llm-usage-monitor.hangfire.antiforgery";

	/// <summary>
	///     The roles are copied into the cookie at sign-in: a revoked role stays valid until the cookie expires, so it is short
	///     and never extended by the activity.
	/// </summary>
	public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);

	public static AuthenticationBuilder AddHangfireDashboardSignIn(this AuthenticationBuilder builder)
	{
		builder.AddCookie(CookieScheme, options =>
		{
			options.Cookie.Name = CookieName;
			options.Cookie.Path = "/hangfire";
			options.Cookie.HttpOnly = true;
			// Lax, not Strict: the sign-in ends with a redirect chain started by Keycloak, which a Strict cookie would not follow.
			options.Cookie.SameSite = SameSiteMode.Lax;
			options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
			options.ExpireTimeSpan = CookieLifetime;
			options.SlidingExpiration = false;
			options.ForwardChallenge = OidcScheme;
			options.Events.OnRedirectToAccessDenied = context =>
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return Task.CompletedTask;
			};
		});
		builder.AddOpenIdConnect(OidcScheme, _ => { });

		// Only the dashboard asks for antiforgery tokens: the API is called with a bearer token, never with a cookie.
		builder.Services.AddAntiforgery(options =>
		{
			options.Cookie.Name = AntiforgeryCookieName;
			options.Cookie.Path = "/hangfire";
			options.Cookie.SameSite = SameSiteMode.Strict;
			options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
		});

		builder.Services.AddOptions<OpenIdConnectOptions>(OidcScheme)
			.Configure<IOptions<OidcConfig>, IHostEnvironment>((options, oidc, environment) =>
			{
				options.SignInScheme = CookieScheme;
				options.Authority = oidc.Value.Issuer;
				options.ClientId = oidc.Value.ClientId;
				options.ResponseType = OpenIdConnectResponseType.Code;
				options.UsePkce = true;
				// .NET uses pushed authorization requests when the provider advertises them; Keycloak rejects them for this
				// secretless public client, so the classic redirect is kept.
				options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
				options.CallbackPath = "/signin-oidc";
				options.RequireHttpsMetadata = !environment.IsDevelopment();
				options.MapInboundClaims = false;
				options.SaveTokens = false;
				options.Scope.Clear();
				options.Scope.Add("openid");
				// Client roles are only in the access token: copy its resource_access claim for the admin requirement.
				options.Events.OnTokenValidated = context =>
				{
					if (context.TokenEndpointResponse?.AccessToken is { } accessToken && context.Principal?.Identity is ClaimsIdentity identity)
					{
						var resourceAccess = new JsonWebToken(accessToken).Claims.FirstOrDefault(claim => claim.Type == "resource_access");
						if (resourceAccess is { })
						{
							identity.AddClaim(new(resourceAccess.Type, resourceAccess.Value, resourceAccess.ValueType));
						}
					}

					return Task.CompletedTask;
				};
			});

		return builder;
	}

	public static AuthorizationBuilder AddHangfireDashboardPolicy(this AuthorizationBuilder builder)
	{
		return builder.AddPolicy(PolicyName, policy => policy
			.AddAuthenticationSchemes(CookieScheme)
			.RequireAuthenticatedUser()
			.AddRequirements(new AdminRoleRequirement()));
	}
}