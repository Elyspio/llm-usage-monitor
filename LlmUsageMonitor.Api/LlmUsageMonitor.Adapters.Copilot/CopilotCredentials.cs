using System.Text.Json;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Adapters.Copilot;

/// <summary>
///     The GitHub token of the Copilot CLI, with the CLI precedence: <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c>,
///     <c>GITHUB_TOKEN</c>, then the login stored by <c>copilot login</c>. Without a system keyring (a Linux service), the
///     CLI stores it in <c>config.json</c>, under <c>authTokens["host:login"].token</c>.
/// </summary>
internal static class CopilotCredentials
{
	private static readonly string[] EnvironmentVariables = ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"];

	public static async Task<string> Read(string configPath, Func<string, string?> environment, CancellationToken cancellationToken)
	{
		foreach (var variable in EnvironmentVariables)
		{
			if (environment(variable) is { Length: > 0 } token) return token;
		}

		string content;
		try
		{
			content = await File.ReadAllTextAsync(configPath, cancellationToken);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			throw new ProviderException(ProviderErrorCode.CredentialsUnavailable, $"The Copilot CLI is not signed in: {configPath} is missing.", exception);
		}
		catch (UnauthorizedAccessException exception)
		{
			throw new ProviderException(ProviderErrorCode.CredentialsUnavailable, $"The Copilot CLI configuration {configPath} cannot be read.", exception);
		}

		return Parse(content)
		       ?? throw new ProviderException(ProviderErrorCode.AuthRequired, "The Copilot CLI is not signed in, or keeps its token in the system keyring.");
	}

	/// <summary>The token of the last signed-in user; <c>null</c> when the file stores none.</summary>
	public static string? Parse(string content)
	{
		try
		{
			// The CLI writes comment lines above the JSON object.
			using var document = JsonDocument.Parse(content, new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("authTokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object
			    || !root.TryGetProperty("lastLoggedInUser", out var user) || user.ValueKind != JsonValueKind.Object || Text(user, "login") is not { } login)
			{
				return null;
			}

			var host = Text(user, "host") ?? "https://github.com";
			if (!tokens.TryGetProperty($"{host}:{login}", out var entry)) return null;
			// CLI 1.0.114 writes { "token": "..." }; a bare string is accepted too.
			return entry.ValueKind switch
			{
				JsonValueKind.String when entry.GetString() is { Length: > 0 } value => value,
				JsonValueKind.Object => Text(entry, "token"),
				_ => null
			};
		}
		catch (JsonException exception)
		{
			throw new ProviderException(ProviderErrorCode.CredentialsUnavailable, "The Copilot CLI configuration is not valid JSON.", exception);
		}
	}

	private static string? Text(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
