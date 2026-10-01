using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Core.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class ReadinessTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Readings_older_than_three_intervals_degrade_then_fail_the_readiness()
	{
		var harness = new TestHarness();
		var check = new PollingHealthCheck(harness.States, harness.Settings, harness.Time);
		await harness.States.Save(new(Provider.Claude) { LastSuccessAt = Start }, Token);
		await harness.States.Save(new(Provider.Codex) { LastSuccessAt = Start.AddMinutes(5) }, Token);

		harness.Time.SetUtcNow(Start.AddMinutes(9));
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Healthy);
		harness.Time.SetUtcNow(Start.AddMinutes(10));
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Degraded);
		harness.Time.SetUtcNow(Start.AddMinutes(15));
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Unhealthy);
	}

	[Fact]
	public void A_rate_limited_provider_counts_from_the_end_of_its_backoff()
	{
		var state = new ProviderState(Provider.Codex) { LastSuccessAt = Start, BackoffUntil = Start.AddMinutes(60) };

		PollingHealthCheck.IsStale(state, 3, Start.AddMinutes(65)).ShouldBeFalse();
		PollingHealthCheck.IsStale(state, 3, Start.AddMinutes(70)).ShouldBeTrue();
		PollingHealthCheck.IsStale(new(Provider.Codex), 3, Start).ShouldBeTrue();
	}

	[Fact]
	public async Task A_job_server_without_a_recent_heartbeat_fails_the_readiness()
	{
		var harness = new TestHarness();
		var monitor = new FakeJobServerMonitor { LastHeartbeat = Start };
		var check = new JobServerHealthCheck(monitor, harness.Time);

		harness.Time.SetUtcNow(Start.AddMinutes(2));
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Healthy);
		harness.Time.SetUtcNow(Start.AddMinutes(3));
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Unhealthy);
		monitor.LastHeartbeat = null;
		(await check.CheckHealthAsync(new(), Token)).Status.ShouldBe(HealthStatus.Unhealthy);
	}

	private sealed class FakeJobServerMonitor : Abstractions.Interfaces.Adapters.IJobServerMonitor
	{
		public DateTimeOffset? LastHeartbeat { get; set; }

		public Task<DateTimeOffset?> GetLastHeartbeat(CancellationToken cancellationToken)
		{
			return Task.FromResult(LastHeartbeat);
		}
	}
}
