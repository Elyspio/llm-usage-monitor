using System.Net;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Adapters.Copilot;

/// <summary>
///     GitHub Copilot adapter: usage reader only (monthly premium requests, with the CLI login). Copilot has no rolling
///     window to open, hence no prompt runner.
/// </summary>
public sealed class CopilotAdapterModule : IModule
{
	public const string HttpClientName = "copilot";

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<CopilotOptions>(configuration.GetSection(CopilotOptions.Section));
		var client = services.AddHttpClient(HttpClientName, (provider, client) =>
			{
				client.BaseAddress = new(provider.GetRequiredService<IOptions<CopilotOptions>>().Value.ApiUrl);
				// GitHub refuses API requests without a user agent.
				client.DefaultRequestHeaders.UserAgent.ParseAdd("llm-usage-monitor");
			})
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
		client.AddStandardResilienceHandler(options =>
		{
			// A 429 goes to the reading backoff: retrying it would only extend the rate limiting.
			options.Retry.ShouldHandle = arguments => ValueTask.FromResult(
				arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests && HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
			options.Retry.MaxRetryAttempts = 2;
			options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
			options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
		});

		services.AddSingleton<IUsageReader, CopilotUsageReader>();
	}
}

public sealed class CopilotOptions
{
	public const string Section = "Copilot";

	/// <summary>The GitHub API, to change for a GitHub Enterprise host.</summary>
	public string ApiUrl { get; init; } = "https://api.github.com/";

	/// <summary>The CLI directory holding <c>config.json</c>; <c>COPILOT_HOME</c> or <c>~/.copilot</c> by default.</summary>
	public string? ConfigDirectory { get; init; }

	public int ReadTimeoutSeconds { get; init; } = 20;

	internal string ResolveConfigPath()
	{
		var directory = ConfigDirectory;
		if (string.IsNullOrWhiteSpace(directory)) directory = Environment.GetEnvironmentVariable("COPILOT_HOME");
		if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
		return Path.Combine(directory, "config.json");
	}
}
