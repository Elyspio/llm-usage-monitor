using LlmUsageMonitor.Abstractions.Injections;
using Microsoft.AspNetCore.Builder;

namespace LlmUsageMonitor.Abstractions.Extensions;

public static class BuilderExtensions
{
	public static WebApplicationBuilder AddModule<T>(this WebApplicationBuilder builder) where T : IModule
	{
		var module = Activator.CreateInstance<T>();

		module.Load(builder.Services, builder.Configuration);

		return builder;
	}
}
