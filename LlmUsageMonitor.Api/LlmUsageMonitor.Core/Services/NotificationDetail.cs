using System.Text.RegularExpressions;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     The detail of a notification leaves for a third-party server (ntfy.sh by default) and often quotes a CLI error: it is
///     cut short and stripped of what looks like a credential or an email address.
/// </summary>
public static partial class NotificationDetail
{
	public const int MaxLength = 300;
	public const string Redacted = "[redacted]";

	public static string Sanitize(string detail)
	{
		var text = BearerPattern().Replace(detail, $"$1 {Redacted}");
		text = NamedSecretPattern().Replace(text, $"$1{Redacted}");
		text = JwtPattern().Replace(text, Redacted);
		text = ApiKeyPattern().Replace(text, Redacted);
		text = OpaqueValuePattern().Replace(text, Redacted);
		text = EmailPattern().Replace(text, "[email]");
		// One line: control characters and line breaks of a stderr dump become single spaces.
		text = WhitespacePattern().Replace(text, " ").Trim();
		return text.Length <= MaxLength ? text : $"{text[..(MaxLength - 1)].TrimEnd()}…";
	}

	[GeneratedRegex(@"\b(Bearer|Basic)\s+[^\s""',;]+", RegexOptions.IgnoreCase)]
	private static partial Regex BearerPattern();

	/// <summary><c>token=…</c>, <c>"api_key": "…"</c>, <c>password: …</c>: the name stays, the value goes.</summary>
	[GeneratedRegex(@"\b((?:[\w-]*(?:token|secret|password|passwd|api[_-]?key|authorization|cookie))[""']?\s*[:=]\s*[""']?)[^\s""',;&]+", RegexOptions.IgnoreCase)]
	private static partial Regex NamedSecretPattern();

	[GeneratedRegex(@"\beyJ[\w-]+\.[\w-]+\.[\w-]*")]
	private static partial Regex JwtPattern();

	/// <summary>Anthropic and OpenAI keys and tokens (<c>sk-ant-…</c>, <c>sk-proj-…</c>).</summary>
	[GeneratedRegex(@"\bsk-[\w-]{16,}")]
	private static partial Regex ApiKeyPattern();

	/// <summary>Long opaque runs (refresh tokens, hashes, base64); file paths and UUIDs are shorter between their separators.</summary>
	[GeneratedRegex(@"[A-Za-z0-9_\-+/=]{40,}")]
	private static partial Regex OpaqueValuePattern();

	[GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
	private static partial Regex EmailPattern();

	[GeneratedRegex(@"[\s\p{Cc}]+")]
	private static partial Regex WhitespacePattern();
}
