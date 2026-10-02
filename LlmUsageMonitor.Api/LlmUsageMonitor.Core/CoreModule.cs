using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Core.Health;
using LlmUsageMonitor.Core.Hosting;
using LlmUsageMonitor.Core.Services;
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

		services.Scan(selector => selector
			.FromAssemblyOf<CoreModule>()
			.AddClasses(filter => filter.InNamespaceOf<DashboardService>())
			// One instance per class, behind each of its interfaces (TriggerService is both ITriggerService and IAutomaticTrigger).
			.AsSelfWithInterfaces()
			.WithSingletonLifetime()
		);

		services.AddHealthChecks()
			.AddCheck<JobServerHealthCheck>("jobs", tags: [HealthCheckTags.Ready], timeout: TimeSpan.FromSeconds(5))
			.AddCheck<PollingHealthCheck>("polling", tags: [HealthCheckTags.Ready], timeout: TimeSpan.FromSeconds(5));

		// The build-time OpenAPI generation starts the host: it must not reach MongoDB nor schedule jobs.
		if (!OpenApiGeneration.IsRunning)
		{
			services.AddHostedService<AppInitializer>();
		}
	}
}