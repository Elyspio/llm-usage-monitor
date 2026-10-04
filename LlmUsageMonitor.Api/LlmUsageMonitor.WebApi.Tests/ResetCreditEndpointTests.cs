using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class ResetCreditEndpointTests(ResetCreditApiFactory factory) : IClassFixture<ResetCreditApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
	[Theory]
	[InlineData(false, HttpStatusCode.Unauthorized)]
	[InlineData(true, HttpStatusCode.Forbidden)]
	public async Task Earned_reset_consumption_requires_the_admin_role(bool authenticated, HttpStatusCode expected)
	{
		using var client = authenticated ? factory.CreateClientWithRoles() : factory.CreateClient();
		var response = await client.PostAsJsonAsync("/api/providers/codex/reset-credits/consume", new ConsumeResetCreditRequest("credit", Guid.NewGuid().ToString()), Token);
		response.StatusCode.ShouldBe(expected);
	}

	[Fact]
	public async Task Confirmed_manual_consumption_updates_the_dashboard_and_replays_the_same_request()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);
		var request = new ConsumeResetCreditRequest("credit", Guid.NewGuid().ToString());
		var response = await client.PostAsJsonAsync("/api/providers/codex/reset-credits/consume", request, Token);
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		var run = (await response.Content.ReadFromJsonAsync<ResetCreditRun>(Json, Token))!;
		run.Status.ShouldBe(ResetCreditRunStatus.Succeeded);
		var replay = await client.PostAsJsonAsync("/api/providers/codex/reset-credits/consume", request, Token);
		(await replay.Content.ReadFromJsonAsync<ResetCreditRun>(Json, Token))!.Id.ShouldBe(run.Id);
		factory.Adapter.Consumes.ShouldBe(1);
		factory.Scheduler.EnqueuedTriggers.ShouldBeEmpty();
		var dashboard = (await client.GetFromJsonAsync<DashboardSnapshot>("/api/dashboard", Json, Token))!;
		var provider = dashboard.Providers.Single(provider => provider.Provider == Provider.Codex);
		provider.ResetCredits!.Balance!.AvailableCount.ShouldBe(0);
		provider.ResetCredits.RecentRuns.ShouldHaveSingleItem().Id.ShouldBe(run.Id);
		provider.LastReading!.Windows.ShouldHaveSingleItem().UsedPercent.ShouldBe(0);
	}

	[Fact]
	public async Task Invalid_idempotency_keys_and_deadlines_return_validation_problems()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);
		(await client.PostAsJsonAsync("/api/providers/codex/reset-credits/consume", new ConsumeResetCreditRequest("credit", "invalid"), Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
		(await client.PutAsJsonAsync("/api/settings/reset-credits", new ResetCreditSettings(new(false, 0), new(false, 60)), Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Reset_credit_settings_are_opt_in_and_can_be_saved_independently()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);
		(await client.GetFromJsonAsync<ResetCreditSettings>("/api/settings/reset-credits", Token)).ShouldBe(ResetCreditSettings.Default);
		var settings = new ResetCreditSettings(new(false, 15), new(false, 30));
		(await client.PutAsJsonAsync("/api/settings/reset-credits", settings, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
		(await client.GetFromJsonAsync<ResetCreditSettings>("/api/settings/reset-credits", Token)).ShouldBe(settings);
		// Restore shared fixture settings for the other tests.
		await client.PutAsJsonAsync("/api/settings/reset-credits", ResetCreditSettings.Default, Token);
	}
}

public sealed class ResetCreditApiFactory : ApiFactory
{
	public ResetCreditApiAdapter Adapter { get; } = new();
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		base.ConfigureWebHost(builder);
		builder.ConfigureServices(services =>
		{
			services.RemoveAll<IUsageReader>();
			services.RemoveAll<IResetCreditConsumer>();
			services.AddSingleton<IUsageReader>(Adapter);
			services.AddSingleton<IResetCreditConsumer>(Adapter);
		});
	}
}

public sealed class ResetCreditApiAdapter : IUsageReader, IResetCreditConsumer
{
	public Provider Provider => Provider.Codex;
	public int Consumes { get; private set; }
	public Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UsageWindow>>([new("codex/primary", Consumes == 0 ? 35 : 0, null, 10080)]);
	public async Task<ProviderUsage> ReadAccount(CancellationToken cancellationToken) => new(await Read(cancellationToken), new(Consumes == 0 ? 1 : 0,
		Consumes == 0 ? [new("credit", 1, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(-1), "Full reset", true, false, [])] : []));
	public Task<ResetCreditResult> Consume(string creditId, string idempotencyKey, CancellationToken cancellationToken)
	{
		Consumes++;
		return Task.FromResult(new ResetCreditResult(true, "reset"));
	}
}
