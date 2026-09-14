using LlmUsageMonitor.Abstractions.Injections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Adapters.Codex;

/// <summary>
///     Codex adapter: app-server JSON-RPC usage reader and headless prompt. Registered empty until the usage reader is ported.
/// </summary>
public sealed class CodexAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
	}
}
