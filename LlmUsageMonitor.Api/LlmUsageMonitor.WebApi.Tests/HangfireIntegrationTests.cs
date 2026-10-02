using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

/// <summary>
///     The API with the real Hangfire: storage in MongoDB and a job server running the jobs. Only the provider adapters are
///     fakes, so no job ever reaches a real account.
/// </summary>
public sealed class HangfireApiFactory : ApiFactory
{
	public FakeProvider Claude { get; } = new(Provider.Claude);

	public FakeProvider Codex { get; } = new(Provider.Codex);

	protected override bool HangfireEnabled => true;

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		base.ConfigureWebHost(builder);
		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IUsageReader>();
			services.RemoveAll<IPromptRunner>();
			foreach (var provider in new[] { Claude, Codex })
			{
				services.AddSingleton<IUsageReader>(provider);
				services.AddSingleton<IPromptRunner>(provider);
			}

			services.RemoveAll<IClaudeSession>();
			services.AddSingleton<IClaudeSession>(Claude);
			services.RemoveAll<IModelPriceSource>();
			services.AddSingleton<IModelPriceSource>(Claude);
		});
	}
}

[Collection(HangfireCollection.Name)]
public sealed class HangfireIntegrationTests(HangfireApiFactory factory) : IClassFixture<HangfireApiFactory>
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task The_polls_queued_at_start_run_on_the_job_server_and_recur_on_their_interval()
	{
		var states = factory.Services.GetRequiredService<IProviderStateRepository>();

		foreach (var provider in Enum.GetValues<Provider>())
		{
			await Eventually(async () => (await states.Get(provider, Token)).LastSuccessAt is { });
		}

		factory.Claude.Reads.ShouldBeGreaterThan(0);
		factory.Codex.Reads.ShouldBeGreaterThan(0);
		using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
		var recurring = connection.GetRecurringJobs().ToDictionary(job => job.Id, job => job.Cron);
		recurring["poll-claude"].ShouldBe("*/3 * * * *");
		recurring["poll-codex"].ShouldBe("*/3 * * * *");
		recurring.ShouldContainKey("refresh-model-prices");
		recurring.ShouldContainKey("purge-failed-jobs");
		// The keep-alive of the Claude login is a scheduled job, four minutes before the token expiry.
		(await states.Get(Provider.Claude, Token)).KeepAlive.ShouldNotBeNull().RunAt.ShouldBe(factory.Claude.ExpiresAt.AddMinutes(-4));
	}

	[Fact]
	public async Task The_readiness_sees_the_heartbeat_of_the_job_server()
	{
		using var client = factory.CreateClient();

		await Eventually(async () => (await client.GetAsync("/health/ready", Token)).StatusCode == HttpStatusCode.OK);
	}

	[Fact]
	public async Task A_manual_trigger_is_run_by_the_job_server()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var accepted = await client.PostAsync("/api/providers/codex/trigger", null, Token);
		accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
		var id = (await accepted.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetString();

		await Eventually(async () =>
			(await client.GetFromJsonAsync<JsonElement>($"/api/trigger-runs/{id}", Token)).GetProperty("status").GetString() == "succeeded");
		factory.Codex.Prompts.ShouldContain(model => model.Length > 0);
	}

	private static async Task Eventually(Func<Task<bool>> condition)
	{
		var deadline = DateTimeOffset.UtcNow + Patience;
		while (!await condition())
		{
			DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the job server did not run the job in time");
			await Task.Delay(TimeSpan.FromMilliseconds(250), Token);
		}
	}
}

/// <summary>
///     The test classes that configure Hangfire run one after the other: Hangfire keeps part of its configuration in statics.
/// </summary>
public static class HangfireCollection
{
	public const string Name = "Hangfire";
}

/// <summary>A provider that answers at once: a used window, a valid login and a successful prompt.</summary>
public sealed class FakeProvider(Provider provider) : IUsageReader, IPromptRunner, IClaudeSession, IModelPriceSource
{
	private int _reads;

	public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds());

	public int Reads => _reads;

	public List<string> Prompts { get; } = [];

	public Task<IReadOnlyList<ModelPrice>> Fetch(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<ModelPrice>>([]);
	}

	public Task<ClaudeTokenInfo> ReadToken(CancellationToken cancellationToken)
	{
		return Task.FromResult(new ClaudeTokenInfo(ExpiresAt, ExpiresAt.AddDays(30)));
	}

	public Task RefreshThroughCli(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public Provider Provider => provider;

	public Task Run(string model, CancellationToken cancellationToken)
	{
		lock (Prompts)
		{
			Prompts.Add(model);
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _reads);
		return Task.FromResult<IReadOnlyList<UsageWindow>>([new($"{provider}/session".ToLowerInvariant(), 40, DateTimeOffset.UtcNow.AddHours(2), 300)]);
	}
}
