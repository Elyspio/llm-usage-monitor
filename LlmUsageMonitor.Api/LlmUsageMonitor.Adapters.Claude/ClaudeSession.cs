using System.Text.Json;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Claude;

internal sealed record ClaudeCredentials(string AccessToken, DateTimeOffset? ExpiresAt, DateTimeOffset? RefreshTokenExpiresAt);

internal static class ClaudeCredentialsFile
{
	/// <summary>
	///     Reads <c>claudeAiOauth</c> from the CLI credentials; expiries are Unix milliseconds. The file is never written.
	/// </summary>
	public static async Task<ClaudeCredentials> Read(string path, CancellationToken cancellationToken)
	{
		JsonDocument document;
		try
		{
			await using var stream = File.OpenRead(path);
			document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			throw new ProviderException(ProviderErrorCodes.CredentialsUnavailable, "Cannot read the Claude CLI credentials. Sign in with `claude auth login` or set Claude:CredentialsPath.", exception);
		}

		using (document)
		{
			var root = document.RootElement;
			var oauth = root.TryGetProperty("claudeAiOauth", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
			if (!oauth.TryGetProperty("accessToken", out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(token.GetString()))
			{
				throw new ProviderException(ProviderErrorCodes.AuthRequired, "The Claude CLI credentials contain no OAuth access token. Sign in with `claude auth login`.");
			}

			return new ClaudeCredentials(token.GetString()!, ReadMilliseconds(oauth, "expiresAt"), ReadMilliseconds(oauth, "refreshTokenExpiresAt"));
		}
	}

	private static DateTimeOffset? ReadMilliseconds(JsonElement oauth, string name) =>
		oauth.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds)
			? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
			: null;
}

/// <summary>
///     The CLI refreshes its token only when it expires within five minutes, and only if the refresh is awaited before the
///     command exits: <c>claude auth status</c> exits first, <c>claude mcp list</c> awaits it (checked on Claude Code 2.1.270).
/// </summary>
internal sealed class ClaudeSession(IOptions<ClaudeOptions> options) : IClaudeSession
{
	public async Task<ClaudeTokenInfo> ReadToken(CancellationToken cancellationToken)
	{
		var credentials = await ClaudeCredentialsFile.Read(options.Value.ResolveCredentialsPath(), cancellationToken);
		return new ClaudeTokenInfo(credentials.ExpiresAt, credentials.RefreshTokenExpiresAt);
	}

	public Task RefreshThroughCli(CancellationToken cancellationToken)
	{
		var settings = options.Value;
		return CliProcess.Run(settings.Executable, ["mcp", "list"], settings.ResolveWorkingDirectory(), TimeSpan.FromSeconds(settings.RefreshTimeoutSeconds), cancellationToken);
	}
}
