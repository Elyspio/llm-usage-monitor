using System.Text.Json;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Classifies the Codex errors on their structure (<c>codexErrorInfo</c>, HTTP status, JSON-RPC code, account state), never
///     on their text.
/// </summary>
public static class CodexErrors
{
	/// <summary>
	///     Maps a <c>codexErrorInfo</c>: either a string or an object keyed by the error kind, whose value may carry the upstream
	///     <c>httpStatusCode</c>. <c>null</c> when it says nothing known.
	/// </summary>
	public static string? FromErrorInfo(JsonElement info)
	{
		string? kind = null;
		JsonElement details = default;
		if (info.ValueKind == JsonValueKind.String)
		{
			kind = info.GetString();
		}
		else if (info.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in info.EnumerateObject())
			{
				(kind, details) = (property.Name, property.Value);
				break;
			}
		}

		return kind switch
		{
			"unauthorized" => ProviderErrorCodes.AuthExpired,
			"usageLimitExceeded" => ProviderErrorCodes.UsageLimit,
			"rateLimitExceeded" => ProviderErrorCodes.RateLimited,
			"serverOverloaded" => ProviderErrorCodes.Overloaded,
			_ => FromHttpStatus(details)
		};
	}

	/// <summary>
	///     Maps a JSON-RPC error: an unknown method or parameters means the CLI changed its protocol; otherwise its <c>data</c>
	///     may hold a <c>codexErrorInfo</c> or an <c>httpStatusCode</c>.
	/// </summary>
	public static string? FromRpcError(CodexRpcException exception)
	{
		if (exception.IsProtocolMismatch)
		{
			return ProviderErrorCodes.CliUnsupportedOption;
		}

		var data = exception.ErrorData;
		if (data.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return data.TryGetProperty("codexErrorInfo", out var info) ? FromErrorInfo(info) : FromHttpStatus(data);
	}

	/// <summary>
	///     The <c>account/read</c> answer of a CLI that lost its login: no account while the OpenAI login is required.
	/// </summary>
	public static bool IsSignedOut(JsonElement account)
	{
		return account.ValueKind == JsonValueKind.Object
		       && account.TryGetProperty("account", out var value) && value.ValueKind == JsonValueKind.Null
		       && account.TryGetProperty("requiresOpenaiAuth", out var required) && required.ValueKind == JsonValueKind.True;
	}

	private static string? FromHttpStatus(JsonElement details)
	{
		int? status = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("httpStatusCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value)
			? value
			: null;
		return status switch
		{
			401 or 403 => ProviderErrorCodes.AuthExpired,
			429 => ProviderErrorCodes.RateLimited,
			503 or 529 => ProviderErrorCodes.Overloaded,
			_ => null
		};
	}
}
