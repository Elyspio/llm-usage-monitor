using LlmUsageMonitor.Abstractions.Injections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Adapters.Claude;

/// <summary>
///     Claude Code adapter: usage reader and headless prompt. Registered empty until the usage reader is ported.
/// </summary>
public sealed class ClaudeAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
	}
}
