using System.Net;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Liveness_is_anonymous_and_checks_nothing_else()
	{
		using var client = factory.CreateClient();
		factory.JobServer.LastHeartbeat = () => null;

		try
		{
			var response = await client.GetAsync("/health/live", Token);

			response.StatusCode.ShouldBe(HttpStatusCode.OK);
			(await response.Content.ReadAsStringAsync(Token)).ShouldBe("Healthy");
		}
		finally
		{
			factory.JobServer.LastHeartbeat = () => DateTimeOffset.UtcNow;
		}
	}

	[Fact]
	public async Task Readiness_is_healthy_with_mongo_a_beating_job_server_and_recent_readings()
	{
		await SetLastSuccess(Provider.Claude, TimeSpan.FromMinutes(1));
		await SetLastSuccess(Provider.Codex, TimeSpan.FromMinutes(2));

		var (status, body) = await GetReady();

		status.ShouldBe(HttpStatusCode.OK);
		body.ShouldBe("Healthy");
	}

	[Fact]
	public async Task One_provider_without_recent_reading_is_degraded()
	{
		await SetLastSuccess(Provider.Claude, TimeSpan.FromMinutes(1));
		await SetLastSuccess(Provider.Codex, TimeSpan.FromMinutes(30));

		var (status, body) = await GetReady();

		status.ShouldBe(HttpStatusCode.OK);
		body.ShouldBe("Degraded");
	}

	[Fact]
	public async Task Frozen_polls_make_readiness_fail()
	{
		await SetLastSuccess(Provider.Claude, TimeSpan.FromMinutes(30));
		await SetLastSuccess(Provider.Codex, TimeSpan.FromHours(2));

		var (status, body) = await GetReady();

		status.ShouldBe(HttpStatusCode.ServiceUnavailable);
		body.ShouldBe("Unhealthy");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task A_stopped_job_server_makes_readiness_fail(bool neverStarted)
	{
		await SetLastSuccess(Provider.Claude, TimeSpan.FromMinutes(1));
		await SetLastSuccess(Provider.Codex, TimeSpan.FromMinutes(1));
		factory.JobServer.LastHeartbeat = () => neverStarted ? null : DateTimeOffset.UtcNow.AddMinutes(-10);

		try
		{
			var (status, body) = await GetReady();

			status.ShouldBe(HttpStatusCode.ServiceUnavailable);
			body.ShouldBe("Unhealthy");
		}
		finally
		{
			factory.JobServer.LastHeartbeat = () => DateTimeOffset.UtcNow;
		}
	}

	private async Task<(HttpStatusCode Status, string Body)> GetReady()
	{
		using var client = factory.CreateClient();
		var response = await client.GetAsync("/health/ready", Token);
		return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
	}

	private async Task SetLastSuccess(Provider provider, TimeSpan age)
	{
		var states = factory.Services.GetRequiredService<IProviderStateRepository>();
		await states.Save(new(provider) { LastSuccessAt = DateTimeOffset.UtcNow - age }, Token);
	}
}
