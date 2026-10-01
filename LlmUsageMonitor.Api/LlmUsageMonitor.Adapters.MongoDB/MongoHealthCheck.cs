using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LlmUsageMonitor.Adapters.MongoDB;

/// <summary>
///     Readiness: the application database answers a ping.
/// </summary>
internal sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
	public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		try
		{
			await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
			return HealthCheckResult.Healthy();
		}
		catch (Exception exception) when (exception is MongoException or TimeoutException)
		{
			return HealthCheckResult.Unhealthy("MongoDB does not answer.", exception);
		}
	}
}
