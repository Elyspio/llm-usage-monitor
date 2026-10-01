using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LlmUsageMonitor.Core.Health;

/// <summary>
///     Readiness: a job server writes its heartbeat. Hangfire beats every 30 s; past two minutes, no job runs anymore.
/// </summary>
public sealed class JobServerHealthCheck(IJobServerMonitor monitor, TimeProvider time) : IHealthCheck
{
	public static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(2);

	public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		var heartbeat = await monitor.GetLastHeartbeat(cancellationToken);
		if (heartbeat is not { } last)
		{
			return HealthCheckResult.Unhealthy("No job server is running.");
		}

		return time.GetUtcNow() - last > MaxHeartbeatAge
			? HealthCheckResult.Unhealthy($"The job server has not beaten since {last:O}.")
			: HealthCheckResult.Healthy();
	}
}

/// <summary>
///     Readiness: each provider had a successful reading within three poll intervals. One stale provider is degraded (its
///     own failures are already notified); all of them stale means the polls no longer run.
/// </summary>
public sealed class PollingHealthCheck(IProviderStateRepository states, ISettingsService settingsService, TimeProvider time) : IHealthCheck
{
	public const int MaxMissedIntervals = 3;

	public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		var settings = await settingsService.Get(cancellationToken);
		var now = time.GetUtcNow();
		var stale = new List<Provider>();
		foreach (var provider in Enum.GetValues<Provider>())
		{
			if (IsStale(await states.Get(provider, cancellationToken), settings.Polling.For(provider), now))
			{
				stale.Add(provider);
			}
		}

		if (stale.Count == 0)
		{
			return HealthCheckResult.Healthy();
		}

		var description = $"No recent reading: {string.Join(", ", stale)}.";
		return stale.Count == Enum.GetValues<Provider>().Length ? HealthCheckResult.Unhealthy(description) : HealthCheckResult.Degraded(description);
	}

	/// <summary>
	///     A rate limited provider is read again once its backoff ends: it counts from then.
	/// </summary>
	public static bool IsStale(ProviderState state, int intervalMinutes, DateTimeOffset now)
	{
		var reference = new[] { state.LastSuccessAt, state.BackoffUntil }.Max();
		return reference is not { } since || now - since > TimeSpan.FromMinutes(intervalMinutes * MaxMissedIntervals);
	}
}
