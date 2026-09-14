using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Adapters.MongoDB.Repositories;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

public sealed class MongoAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		MongoConventions.Register();

		var connectionString = configuration.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is required.");
		var databaseName = MongoUrl.Create(connectionString).DatabaseName ?? "llm-usage-monitor";

		services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
		services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

		services.AddSingleton<IStorageInitializer, MongoStorageInitializer>();
		services.AddSingleton<IUsageSnapshotRepository, UsageSnapshotRepository>();
		services.AddSingleton<IResetRepository, ResetRepository>();
		services.AddSingleton<ITriggerRunRepository, TriggerRunRepository>();
		services.AddSingleton<IProviderStateRepository, ProviderStateRepository>();
		services.AddSingleton<ISettingsRepository, SettingsRepository>();

		// Data Protection keys live next to the data they protect (the ntfy token).
		services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
			new ConfigureOptions<KeyManagementOptions>(options => options.XmlRepository = new MongoXmlRepository(sp.GetRequiredService<IMongoDatabase>())));
	}
}
