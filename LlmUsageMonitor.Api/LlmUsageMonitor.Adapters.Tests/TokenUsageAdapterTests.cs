using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Adapters.LiteLlm;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class LiteLlmPriceTests
{
	[Fact]
	public void Only_the_anthropic_and_openai_models_with_both_prices_are_kept()
	{
		var prices = LiteLlmPriceSource.Parse(Fixtures.Load("litellm-prices.json"));

		prices.ShouldBe([
			new ModelPrice("claude-opus-5", 5e-6, 25e-6, 0.5e-6, 6.25e-6),
			new ModelPrice("gpt-6-sol", 2e-6, 10e-6, 0.2e-6, null)
		]);
	}
}

public sealed class TokenUsageRepositoryTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
	private static readonly DateTimeOffset Hour = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_bucket_is_replaced_by_its_key_and_read_back_by_period_and_workstation()
	{
		await using var services = await mongo.CreateServices();
		var usage = services.GetRequiredService<ITokenUsageRepository>();

		await usage.Upsert([
			new("pc-1", Provider.Claude, "claude-opus-5", Hour, new(1, 2, 3, 4), new(1.5, 0.5)),
			new("pc-1", Provider.Codex, "gpt-6-sol", Hour, new(1, 0, 0, 1), null),
			new("pc-2", Provider.Claude, "claude-opus-5", Hour, new(9, 0, 0, 0), null),
			new("pc-1", Provider.Claude, "claude-opus-5", Hour.AddHours(-1), new(7, 0, 0, 0), null)
		], Token);
		await usage.Upsert([new("pc-1", Provider.Claude, "claude-opus-5", Hour, new(10, 20, 30, 40), new(3, 1))], Token);

		var pc1 = await usage.Get(Hour, Hour.AddHours(1), "pc-1", Token);
		var all = await usage.Get(Hour.AddHours(-1), Hour, null, Token);

		pc1.OrderBy(bucket => bucket.Provider).ShouldBe([
			new("pc-1", Provider.Claude, "claude-opus-5", Hour, new(10, 20, 30, 40), new(3, 1)),
			new("pc-1", Provider.Codex, "gpt-6-sol", Hour, new(1, 0, 0, 1), null)
		]);
		all.ShouldHaveSingleItem().Tokens.Input.ShouldBe(7);
	}

	[Fact]
	public async Task Machines_and_prices_are_upserted_by_id()
	{
		await using var services = await mongo.CreateServices();
		var machines = services.GetRequiredService<IUsageMachineRepository>();
		var prices = services.GetRequiredService<IModelPriceRepository>();

		await machines.Save(new("pc-1", "PC", Hour), Token);
		await machines.Save(new("pc-1", "PC bureau", Hour.AddMinutes(5)), Token);
		(await prices.Any(Token)).ShouldBeFalse();
		await prices.Save([new("claude-opus-5", 5e-6, 25e-6, null, null), new("gpt-6-sol", 2e-6, 10e-6, 0.2e-6, null)], Token);
		await prices.Save([new("claude-opus-5", 4e-6, 20e-6, 0.4e-6, 5e-6)], Token);

		(await machines.GetAll(Token)).ShouldBe([new UsageMachine("pc-1", "PC bureau", Hour.AddMinutes(5))]);
		(await prices.Any(Token)).ShouldBeTrue();
		(await prices.Find(["claude-opus-5", "claude-opus-5-20260101"], Token)).ShouldBe([new ModelPrice("claude-opus-5", 4e-6, 20e-6, 0.4e-6, 5e-6)]);
	}
}
