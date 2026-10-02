using System.Security.Claims;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Authorization;

/// <summary>
///     The single policy of the application: the Keycloak client role <see cref="OidcConfig.AdminRole" /> is required everywhere.
/// </summary>
public static class AdminPolicy
{
	public const string Name = "Admin";

	public static AuthorizationBuilder AddAdminPolicy(this AuthorizationBuilder builder)
	{
		builder.Services.AddSingleton<IAuthorizationHandler, AdminRoleHandler>();
		return builder.AddPolicy(Name, policy => policy
			.RequireAuthenticatedUser()
			.AddRequirements(new AdminRoleRequirement()));
	}

	/// <summary>
	///     Reads the client roles from the Keycloak <c>resource_access</c> claim, a JSON object keyed by client identifier.
	///     Any other shape grants nothing: a malformed claim is a 403, never a 500.
	/// </summary>
	public static bool HasClientRole(ClaimsPrincipal user, string clientId, string role)
	{
		foreach (var claim in user.FindAll("resource_access"))
			try
			{
				using var document = JsonDocument.Parse(claim.Value);
				if (document.RootElement.ValueKind == JsonValueKind.Object
				    && document.RootElement.TryGetProperty(clientId, out var client)
				    && client.ValueKind == JsonValueKind.Object
				    && client.TryGetProperty("roles", out var roles)
				    && roles.ValueKind == JsonValueKind.Array
				    && roles.EnumerateArray().Any(candidate => candidate.ValueKind == JsonValueKind.String && candidate.GetString() == role))
				{
					return true;
				}
			}
			catch (JsonException)
			{
				// Not JSON: the other resource_access claims are still checked.
			}

		return false;
	}
}

public sealed class AdminRoleRequirement : IAuthorizationRequirement;

public sealed class AdminRoleHandler(IOptions<OidcConfig> oidc) : AuthorizationHandler<AdminRoleRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRoleRequirement requirement)
	{
		if (AdminPolicy.HasClientRole(context.User, oidc.Value.ClientId, oidc.Value.AdminRole))
		{
			context.Succeed(requirement);
		}

		return Task.CompletedTask;
	}
}