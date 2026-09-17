using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Codex adapter: usage reader and headless prompt, both over the CLI app-server (JSON-RPC on stdio).
/// </summary>
public sealed class CodexAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<CodexOptions>(configuration.GetSection(CodexOptions.Section));
		services.AddSingleton<IUsageReader, CodexUsageReader>();
		services.AddSingleton<IPromptRunner, CodexPromptRunner>();
	}
}

public sealed class CodexOptions
{
	public const string Section = "Codex";

	/// <summary>The native executable, on the PATH by default.</summary>
	public string Executable { get; init; } = "codex";

	/// <summary>An empty directory used as working directory of the CLI; a temporary one by default.</summary>
	public string? WorkingDirectory { get; init; }

	public int ReadTimeoutSeconds { get; init; } = 30;

	public int PromptTimeoutSeconds { get; init; } = 180;

	/// <summary>The reasoning effort of the minimal prompt.</summary>
	public string Effort { get; init; } = "low";

	internal string ResolveWorkingDirectory()
	{
		var directory = WorkingDirectory ?? Path.Combine(Path.GetTempPath(), "llm-usage-monitor", "codex");
		Directory.CreateDirectory(directory);
		return directory;
	}
}