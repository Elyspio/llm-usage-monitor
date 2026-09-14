namespace LlmUsageMonitor.Abstractions.Exceptions;

/// <summary>
///     A failure reported by a provider adapter (reading, prompt or CLI), with a stable code shown to the user.
/// </summary>
public sealed class ProviderException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
	public string Code { get; } = code;
}

public static class ProviderErrorCodes
{
	public const string AuthExpired = "AUTH_EXPIRED";
	public const string AuthRequired = "AUTH_REQUIRED";
	public const string CredentialsUnavailable = "CREDENTIALS_UNAVAILABLE";
	public const string AccessDenied = "ACCESS_DENIED";
	public const string RateLimited = "RATE_LIMITED";
	public const string UsageLimit = "USAGE_LIMIT";
	public const string HttpError = "HTTP_ERROR";
	public const string Timeout = "TIMEOUT";
	public const string FetchFailed = "FETCH_FAILED";
	public const string InvalidResponse = "INVALID_RESPONSE";
	public const string NoUsageData = "NO_USAGE_DATA";
	public const string CliUnavailable = "CLI_UNAVAILABLE";
	public const string CliExited = "CLI_EXITED";
	public const string CliBusy = "CLI_BUSY";
	public const string TriggerFailed = "TRIGGER_FAILED";
	public const string Interrupted = "INTERRUPTED";
}

/// <summary>
///     Invalid input, reported as a 400 with one message per field.
/// </summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors) : Exception("The request is invalid.")
{
	public IDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ResourceNotFoundException(string message) : Exception(message);
