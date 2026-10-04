using System.Net;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     Claude Code adapter: usage reader (OAuth usage endpoint with the CLI login), token refresh and headless prompt.
/// </summary>
public sealed class ClaudeAdapterModule : IModule
{
	public const string HttpClientName = "claude";

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<ClaudeOptions>(configuration.GetSection(ClaudeOptions.Section));
		services.AddHttpClient(HttpClientName, client => client.BaseAddress = new("https://api.anthropic.com/"))
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
			.AddStandardResilienceHandler(options =>
			{
				// A 429 goes to the reading backoff: retrying it would only extend the rate limiting.
				options.Retry.ShouldHandle = arguments => ValueTask.FromResult(
					arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests && HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
				options.Retry.MaxRetryAttempts = 2;
				options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
				options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
			});

		services.AddSingleton<IClaudeSession, ClaudeSession>();
		services.AddSingleton<IUsageReader, ClaudeUsageReader>();
		services.AddSingleton<IResetCreditConsumer, ClaudeResetCreditConsumer>();
		services.AddSingleton<IPromptRunner, ClaudePromptRunner>();
	}
}

public sealed class ClaudeOptions
{
	public const string Section = "Claude";

	/// <summary>The native executable, on the PATH by default.</summary>
	public string Executable { get; init; } = "claude";

	/// <summary>The CLI credentials file; <c>CLAUDE_CONFIG_DIR/.credentials.json</c> or <c>~/.claude/.credentials.json</c> by default.</summary>
	public string? CredentialsPath { get; init; }

	/// <summary>The CLI .claude.json containing oauthAccount.organizationUuid, needed only to consume an earned reset.</summary>
	public string? AccountConfigPath { get; init; }

	internal string ResolveAccountConfigPath() => AccountConfigPath ?? Path.Combine(
		Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

	/// <summary>An empty directory used as working directory of the CLI; a temporary one by default.</summary>
	public string? WorkingDirectory { get; init; }

	public int ReadTimeoutSeconds { get; init; } = 20;

	public int PromptTimeoutSeconds { get; init; } = 120;

	public int RefreshTimeoutSeconds { get; init; } = 60;

	internal string ResolveCredentialsPath()
	{
		if (!string.IsNullOrWhiteSpace(CredentialsPath))
		{
			return CredentialsPath;
		}

		var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
		if (string.IsNullOrWhiteSpace(configDirectory))
		{
			configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
		}

		return Path.Combine(configDirectory, ".credentials.json");
	}

	internal string ResolveWorkingDirectory()
	{
		var directory = WorkingDirectory ?? Path.Combine(Path.GetTempPath(), "llm-usage-monitor", "claude");
		Directory.CreateDirectory(directory);
		return directory;
	}
}
