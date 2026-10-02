using LlmUsageMonitor.Abstractions.Injections;

namespace LlmUsageMonitor.Hosting;

public static class ModuleExtensions
{
	/// <summary>
	///     Loads an application module (core or adapter) into the services of the host.
	/// </summary>
	public static WebApplicationBuilder AddModule<T>(this WebApplicationBuilder builder) where T : IModule, new()
	{
		new T().Load(builder.Services, builder.Configuration);

		return builder;
	}
}
