using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Core;

public sealed class CoreModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<OidcConfig>()
			.Bind(configuration.GetRequiredSection(OidcConfig.Section))
			.ValidateOnStart();

		services.Scan(selector => selector
			.FromAssemblyOf<CoreModule>()
			.AddClasses(filter => filter.InNamespaceOf<DashboardService>())
			.AsImplementedInterfaces()
			.WithSingletonLifetime()
		);
	}
}
