using System.Text.RegularExpressions;
using LlmUsageMonitor.Abstractions.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     The <c>User-Agent</c> of the Claude CLI. Without it the usage endpoint answers <c>ineligible_reason: "surface"</c>
///     and hides the earned resets (checked on Claude Code 2.1.325).
/// </summary>
internal sealed partial class ClaudeUserAgent(IOptions<ClaudeOptions> options, TimeProvider time, ILogger<ClaudeUserAgent> logger)
{
	/// <summary>Used until the installed CLI has given its version once.</summary>
	public const string FallbackVersion = "2.1.325";

	private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

	// Replaced as a whole: two concurrent reads only run `claude --version` twice.
	private volatile Cached? cached;

	/// <summary>Never throws: an unreadable version keeps the last known one.</summary>
	public async Task<string> Get(CancellationToken cancellationToken)
	{
		var current = cached;
		if (current is null || time.GetUtcNow() - current.ReadAt >= Lifetime)
		{
			var settings = options.Value;
			var output = await CliProcess.ReadVersion(settings.Executable, settings.ResolveWorkingDirectory(), logger, cancellationToken);
			var match = VersionPattern().Match(output);
			if (!match.Success)
			{
				logger.LogWarning("Cannot read the Claude CLI version ({Output}): the User-Agent keeps {Version}", output, current?.Version ?? FallbackVersion);
			}

			cached = current = new(match.Success ? match.Value : current?.Version ?? FallbackVersion, time.GetUtcNow());
		}

		return $"claude-cli/{current.Version} (external, cli)";
	}

	[GeneratedRegex(@"^\d+\.\d+\.\d+")]
	private static partial Regex VersionPattern();

	private sealed record Cached(string Version, DateTimeOffset ReadAt);
}

/// <summary>Sets the CLI <c>User-Agent</c> on every request of the Claude client: the usage read and the reset consumption.</summary>
internal sealed class ClaudeUserAgentHandler(ClaudeUserAgent userAgent) : DelegatingHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		request.Headers.UserAgent.Clear();
		request.Headers.TryAddWithoutValidation("User-Agent", await userAgent.Get(cancellationToken));
		return await base.SendAsync(request, cancellationToken);
	}
}
