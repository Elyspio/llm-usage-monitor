using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Adapters.MongoDB.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     The application storage: one <see cref="IMongoClient" /> and its <see cref="IMongoDatabase" />, shared with the
///     Hangfire storage; the repositories; and the Data Protection key ring, next to the data it protects (the ntfy token).
/// </summary>
public sealed class MongoAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		MongoConventions.Register();

		var connectionString = configuration.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is required.");
		// The connection string of the Aspire resource names no database: the default one is used then.
		var databaseName = MongoUrl.Create(connectionString).DatabaseName ?? StorageDefaults.DatabaseName;
		services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
		services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

		services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: [HealthCheckTags.Ready], timeout: TimeSpan.FromSeconds(5));

		services.AddSingleton<IStorageInitializer, MongoStorageInitializer>();
		services.AddSingleton<IUsageSnapshotRepository, UsageSnapshotRepository>();
		services.AddSingleton<IResetRepository, ResetRepository>();
		services.AddSingleton<ITriggerRunRepository, TriggerRunRepository>();
		services.AddSingleton<IProviderStateRepository, ProviderStateRepository>();
		services.AddSingleton<ISettingsRepository, SettingsRepository>();
		services.AddSingleton<ITokenUsageRepository, TokenUsageRepository>();
		services.AddSingleton<IUsageMachineRepository, UsageMachineRepository>();
		services.AddSingleton<IModelPriceRepository, ModelPriceRepository>();

		services.AddDataProtection().SetApplicationName("llm-usage-monitor");
		services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
		// The build-time OpenAPI generation must not reach MongoDB: its key ring stays in memory.
		services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp => new ConfigureOptions<KeyManagementOptions>(options =>
			options.XmlRepository = OpenApiGeneration.IsRunning ? new TransientXmlRepository() : new MongoXmlRepository(sp.GetRequiredService<IMongoDatabase>())));
	}
}
