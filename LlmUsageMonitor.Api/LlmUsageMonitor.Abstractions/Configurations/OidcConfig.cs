namespace LlmUsageMonitor.Abstractions.Configurations;

/// <summary>
///     Represents the OpenID Connect configuration.
/// </summary>
public sealed class OidcConfig
{
	/// <summary>
	///     The configuration section name.
	/// </summary>
	public const string Section = "Oidc";

	/// <summary>
	///     The realm URL, used as token issuer and metadata authority.
	/// </summary>
	public required string Authority { get; init; }

	/// <summary>
	///     The public client of the application (SPA, Swagger UI, Hangfire dashboard), which also holds the admin role.
	/// </summary>
	public required string ClientId { get; init; }

	/// <summary>
	///     The other clients whose access tokens the API accepts (their <c>azp</c> claim), besides <see cref="ClientId" />: the
	///     standalone collector and Elytools upload the token usage with their own Keycloak client.
	/// </summary>
	public string[] AuthorizedParties { get; init; } = [];

	/// <summary>
	///     The client role required by every endpoint.
	/// </summary>
	public string AdminRole { get; init; } = "llm-usage-monitor:admin";

	/// <summary>
	///     The issuer of the tokens: the authority without its trailing slash, as Keycloak writes it in the <c>iss</c> claim.
	/// </summary>
	public string Issuer => Authority.TrimEnd('/');

	/// <summary>
	///     Whether an access token issued to <paramref name="authorizedParty" /> is accepted.
	/// </summary>
	public bool IsAuthorizedParty(string? authorizedParty)
	{
		return authorizedParty is { Length: > 0 } && (authorizedParty == ClientId || AuthorizedParties.Contains(authorizedParty, StringComparer.Ordinal));
	}
}
