using System.ComponentModel;
using Hangfire;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Services;

namespace LlmUsageMonitor.Adapters.Hangfire;

internal sealed class HangfireJobScheduler(IBackgroundJobClient jobs, IRecurringJobManager recurringJobs) : IJobScheduler
{
	public void SetPollInterval(Provider provider, int minutes) => recurringJobs.AddOrUpdate<ProviderJobs>(
		$"poll-{provider.ToString().ToLowerInvariant()}",
		job => job.Poll(provider, CancellationToken.None),
		ToCron(minutes),
		new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

	public void EnqueuePoll(Provider provider) => jobs.Enqueue<ProviderJobs>(job => job.Poll(provider, CancellationToken.None));

	public string SchedulePostResetCheck(Provider provider, DateTimeOffset runAt) =>
		jobs.Schedule<ProviderJobs>(job => job.PostResetCheck(provider, CancellationToken.None), runAt);

	public string ScheduleKeepAlive(DateTimeOffset runAt) => jobs.Schedule<ProviderJobs>(job => job.KeepAlive(CancellationToken.None), runAt);

	public void EnqueueTrigger(string runId) => jobs.Enqueue<ProviderJobs>(job => job.RunTrigger(runId, CancellationToken.None));

	public void Delete(string jobId) => jobs.Delete(jobId);

	internal static string ToCron(int minutes) => minutes >= 60 ? "0 * * * *" : $"*/{Math.Max(1, minutes)} * * * *";
}

/// <summary>
///     The Hangfire entry points; each one delegates to an application service.
/// </summary>
public sealed class ProviderJobs(IUsageMonitor monitor, ITriggerService triggers, IClaudeKeepAlive keepAlive)
{
	[DisplayName("Poll {0}")]
	public Task Poll(Provider provider, CancellationToken cancellationToken) => monitor.Poll(provider, cancellationToken);

	[DisplayName("Post-reset check {0}")]
	public Task PostResetCheck(Provider provider, CancellationToken cancellationToken) => monitor.Poll(provider, cancellationToken);

	[DisplayName("Claude token keep-alive")]
	public Task KeepAlive(CancellationToken cancellationToken) => keepAlive.Run(cancellationToken);

	[DisplayName("Manual trigger {0}")]
	public Task RunTrigger(string runId, CancellationToken cancellationToken) => triggers.ExecuteManual(runId, cancellationToken);
}
