using LlmUsageMonitor.Abstractions.Injections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

public sealed class MongoAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		var connectionString = configuration.GetConnectionString("MongoDB") ?? throw new InvalidOperationException("ConnectionStrings:MongoDB is required.");
		var databaseName = MongoUrl.Create(connectionString).DatabaseName ?? "llm-usage-monitor";

		services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
		services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
	}
}
