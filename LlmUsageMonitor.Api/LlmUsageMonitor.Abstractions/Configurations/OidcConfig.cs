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
	///     The public client identifier, which is also the expected access token audience.
	/// </summary>
	public required string ClientId { get; init; }

	/// <summary>
	///     The client role required by every endpoint.
	/// </summary>
	public string AdminRole { get; init; } = "llm-usage-monitor:admin";
}