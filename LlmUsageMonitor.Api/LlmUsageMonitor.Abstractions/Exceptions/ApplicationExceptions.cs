namespace LlmUsageMonitor.Abstractions.Exceptions;

/// <summary>
///     A failure reported by a provider adapter (reading, prompt or CLI), with a stable code shown to the user.
/// </summary>
public sealed class ProviderException(ProviderErrorCode code, string message, Exception? innerException = null) : Exception(message, innerException)
{
	public ProviderErrorCode Code { get; } = code;
}

/// <summary>
///     The stable failure codes of the providers, shown on the dashboard and in the alerts.
/// </summary>
public enum ProviderErrorCode
{
	AuthExpired,
	AuthRequired,
	CredentialsUnavailable,
	AccessDenied,
	RateLimited,
	UsageLimit,
	HttpError,
	Timeout,
	FetchFailed,
	InvalidResponse,
	NoUsageData,
	CliUnavailable,
	CliExited,
	CliBusy,
	TriggerFailed,
	Interrupted,
	Overloaded,
	Cancelled,
	UnexpectedError,

	/// <summary>The CLI updated itself and rejects an option or a protocol method the adapter uses; the message carries its version.</summary>
	CliUnsupportedOption,

	/// <summary>A notification could not be sent.</summary>
	NotificationFailed
}

public static class ProviderErrorCodeStorage
{
	/// <summary>
	///     The upper snake case form (<c>AUTH_EXPIRED</c>) kept by the stored trigger and reset credit runs, and by the problem details.
	/// </summary>
	public static string ToStoredCode(this ProviderErrorCode code)
	{
		return string.Concat(code.ToString().Select((letter, index) => index > 0 && char.IsUpper(letter) ? $"_{letter}" : letter.ToString())).ToUpperInvariant();
	}

	/// <summary>
	///     Reads a stored code back; a code this version does not know is an unexpected error.
	/// </summary>
	public static ProviderErrorCode ParseStoredCode(string? storedCode)
	{
		return Enum.GetValues<ProviderErrorCode>().FirstOrDefault(code => code.ToStoredCode() == storedCode, ProviderErrorCode.UnexpectedError);
	}
}

/// <summary>
///     Invalid input, reported as a 400 with one message per field.
/// </summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors) : Exception("The request is invalid.")
{
	public IDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ResourceNotFoundException(string message) : Exception(message);
