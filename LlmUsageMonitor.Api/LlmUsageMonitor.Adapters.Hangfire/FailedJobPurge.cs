using System.ComponentModel;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Adapters.Hangfire;

/// <summary>
///     Hangfire keeps failed jobs forever (Failed is not a final state, and an expiry set by a filter is overwritten when the
///     job is persisted): once a day, the jobs failed for more than <see cref="Retention" /> are deleted, a final state that
///     expires after a day like the succeeded jobs.
/// </summary>
public sealed class FailedJobPurge(JobStorage storage, IBackgroundJobClient jobs, TimeProvider time, ILogger<FailedJobPurge> logger)
{
	public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

	[DisplayName("Purge failed jobs")]
	public Task Purge(CancellationToken cancellationToken)
	{
		var monitoring = storage.GetMonitoringApi();
		var limit = (time.GetUtcNow() - Retention).UtcDateTime;
		var failed = monitoring.FailedJobs(0, (int)Math.Min(monitoring.FailedCount(), int.MaxValue));

		var deleted = 0;
		foreach (var (id, job) in failed)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (job.FailedAt is { } failedAt && failedAt < limit && jobs.Delete(id))
			{
				deleted++;
			}
		}

		logger.LogInformation("{Count} failed jobs older than {Retention} purged", deleted, Retention);
		return Task.CompletedTask;
	}
}
