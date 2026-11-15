using Elyspio.Utils.Results;

namespace LlmUsageMonitor.Abstractions.Exceptions;

/// <summary>
///     A failure reported by a provider adapter (reading, prompt, reset or CLI), with a stable code shown to the user and
///     stored with the runs. 409 when the provider is busy, 502 otherwise; <see cref="Error.Code" /> is the stored form
///     (<c>AUTH_EXPIRED</c>), also the <c>code</c> of the problem details.
/// </summary>
public sealed record ProviderError(ProviderErrorCode ProviderCode, string Message)
	: Failure(ProviderCode == ProviderErrorCode.CliBusy ? 409 : 502, ProviderCode.ToStoredCode(), Message);

/// <summary>
///     Unwinds a <see cref="ProviderError" /> out of the parsers and CLI helpers of an adapter. It never leaves the
///     adapter: every entry point returns it as a value through <see cref="ProviderResult.Capture{T}" />.
/// </summary>
public sealed class ProviderException(ProviderErrorCode code, string message, Exception? innerException = null) : Exception(message, innerException)
{
	public ProviderErrorCode Code { get; } = code;

	public ProviderError Error => new(Code, Message);
}

public static class ProviderResult
{
	/// <summary>Runs an adapter call and returns its <see cref="ProviderException" /> as a failure. Other exceptions (bugs, cancellation) propagate.</summary>
	public static async Task<Result<T, ProviderError>> Capture<T>(Func<Task<T>> call)
	{
		try
		{
			return await call();
		}
		catch (ProviderException exception)
		{
			return exception.Error;
		}
	}
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

/// <summary>Invalid input, reported as a 400 with one message per field.</summary>
public sealed record RequestInvalid : Invalid
{
	public RequestInvalid(IReadOnlyDictionary<string, string[]> errors) : base("request.invalid", "The request is invalid.")
	{
		Fields = errors;
	}

	public RequestInvalid(string field, string message) : this(new Dictionary<string, string[]> { [field] = [message] })
	{
	}
}

public sealed record ResourceNotFound(string Message) : NotFound("resource.not-found", Message);
