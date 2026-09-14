using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Core.Hosting;
using LlmUsageMonitor.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LlmUsageMonitor.Core;

public sealed class CoreModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<OidcConfig>()
			.Bind(configuration.GetRequiredSection(OidcConfig.Section))
			.ValidateOnStart();
		services.Configure<AppConfig>(configuration.GetSection(AppConfig.Section));

		services.TryAddSingleton(TimeProvider.System);
		services.AddDataProtection().SetApplicationName("llm-usage-monitor");

		services.Scan(selector => selector
			.FromAssemblyOf<CoreModule>()
			.AddClasses(filter => filter.InNamespaceOf<DashboardService>())
			.AsImplementedInterfaces()
			.WithSingletonLifetime()
		);

		// The build-time OpenAPI generation starts the host: it must not reach MongoDB nor schedule jobs.
		if (!OpenApiGeneration.IsRunning) services.AddHostedService<AppInitializer>();
	}
}
