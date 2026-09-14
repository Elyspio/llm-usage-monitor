using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Abstractions.Injections;

/// <summary>
///     Registers an application module with the dependency injection container.
/// </summary>
public interface IModule
{
	/// <summary>
	///     Adds the module's services and configuration bindings.
	/// </summary>
	/// <param name="services">The application service collection.</param>
	/// <param name="configuration">The application configuration.</param>
	public void Load(IServiceCollection services, IConfiguration configuration);
}
