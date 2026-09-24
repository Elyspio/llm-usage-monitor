using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Core.Rules;
using LlmUsageMonitor.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class TokenUsageTests
{
	// 2026-09-25 14:30 in Paris (UTC+2).
	private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 30, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset CurrentHour = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

	private static readonly ModelPrice Opus = new("claude-opus-5", 5e-6, 25e-6, 0.5e-6, 6.25e-6);
	private static readonly ModelPrice Haiku = new("claude-haiku-4-5", 1e-6, 5e-6, 0.1e-6, 1.25e-6);

	private readonly InMemoryTokenUsage _usage = new();
	private readonly InMemoryMachines _machines = new();
	private readonly InMemoryPrices _prices = new();
	private readonly TokenUsageService _service;

	public TokenUsageTests()
	{
		_prices.Stored.AddRange([Opus, Haiku]);
		_service = new(_usage, _machines, _prices, new FakeTimeProvider(Now));
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Cost_uses_the_cache_prices_and_counts_the_savings_against_the_input_price()
	{
		var cost = TokenPricing.Cost(new(1_000_000, 10_000_000, 1_000_000, 100_000), Opus);

		cost.Usd.ShouldBe(5 + 5 + 6.25 + 2.5, 1e-9);
		cost.CacheSavingsUsd.ShouldBe(45, 1e-9);
	}

	[Fact]
	public void Missing_cache_prices_fall_back_to_the_input_price()
	{
		var cost = TokenPricing.Cost(new(0, 1_000_000, 1_000_000, 0), new("gpt-x", 2e-6, 8e-6, null, null));

		cost.Usd.ShouldBe(4, 1e-9);
		cost.CacheSavingsUsd.ShouldBe(0);
	}

	[Fact]
	public async Task Upload_prices_known_models_including_dated_names_and_leaves_the_others_unpriced()
	{
		var result = await _service.Upload(Upload(
			Bucket("claude-opus-5", CurrentHour, new(1_000_000, 0, 0, 0)),
			Bucket("claude-haiku-4-5-20251001", CurrentHour, new(1_000_000, 0, 0, 0)),
			Bucket("<synthetic>", CurrentHour, new(0, 0, 0, 0))), Token);

		result.ShouldBe(new(3, 1));
		_usage.Stored.Values.Single(bucket => bucket.Model == "claude-opus-5").Cost!.Usd.ShouldBe(5, 1e-9);
		_usage.Stored.Values.Single(bucket => bucket.Model == "claude-haiku-4-5-20251001").Cost!.Usd.ShouldBe(1, 1e-9);
		_usage.Stored.Values.Single(bucket => bucket.Model == "<synthetic>").Cost.ShouldBeNull();
		_machines.Stored["pc-1"].ShouldBe(new UsageMachine("pc-1", "PC bureau", Now));
	}

	[Fact]
	public async Task A_new_upload_of_the_same_hour_replaces_the_bucket()
	{
		await _service.Upload(Upload(Bucket("claude-opus-5", CurrentHour, new(100, 0, 0, 10))), Token);
		await _service.Upload(Upload(Bucket("claude-opus-5", CurrentHour, new(300, 0, 0, 30))), Token);
		await _service.Upload(Upload(Bucket("claude-opus-5", CurrentHour, new(300, 0, 0, 30))), Token);

		_usage.Stored.Values.ShouldHaveSingleItem().Tokens.ShouldBe(new(300, 0, 0, 30));
	}

	[Fact]
	public async Task Invalid_buckets_are_rejected_with_one_error_per_field()
	{
		var upload = new TokenUsageUpload("pc-1", " ",
		[
			Bucket("claude-opus-5", CurrentHour.AddMinutes(5), TokenCounts.Zero),
			Bucket("claude-opus-5", CurrentHour.AddHours(1), TokenCounts.Zero),
			Bucket("", CurrentHour, new(-1, 0, 0, 0)),
			Bucket("claude-opus-5", CurrentHour, TokenCounts.Zero),
			Bucket("claude-opus-5", CurrentHour, TokenCounts.Zero)
		]);

		var exception = await Should.ThrowAsync<RequestValidationException>(() => _service.Upload(upload, Token));

		exception.Errors.Keys.ShouldBe(["machineName", "buckets[0].hour", "buckets[1].hour", "buckets[2].model", "buckets[2].tokens", "buckets[4]"], ignoreOrder: true);
		_usage.Stored.ShouldBeEmpty();
	}

	[Fact]
	public async Task The_7_day_report_groups_the_hours_by_local_day()
	{
		// 2026-09-18 22:00 UTC is already the 19th in Paris, first day of the period; 21:00 UTC is still the 18th: left out.
		await _service.Upload(Upload(
			Bucket("claude-opus-5", new(2026, 9, 18, 21, 0, 0, TimeSpan.Zero), new(1, 0, 0, 0)),
			Bucket("claude-opus-5", new(2026, 9, 18, 22, 0, 0, TimeSpan.Zero), new(1_000_000, 0, 0, 0)),
			Bucket("claude-opus-5", new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero), new(1_000_000, 0, 0, 0)),
			Bucket("claude-opus-5", CurrentHour, new(0, 0, 0, 1_000))), Token);

		var report = await _service.Get(TokenUsageRange.Last7Days, null, "Europe/Paris", Token);

		report.Step.ShouldBe(TokenUsageStep.Day);
		report.From.ShouldBe(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.FromHours(2)));
		report.Rows.Select(row => (row.Start, row.Tokens.Input, row.Tokens.Output)).ShouldBe([
			(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.FromHours(2)), 2_000_000L, 0L),
			(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.FromHours(2)), 0L, 1_000L)
		]);
		report.Rows[0].CostUsd!.Value.ShouldBe(10, 1e-9);
	}

	[Fact]
	public async Task The_24_hour_report_is_hourly_and_can_be_filtered_by_workstation()
	{
		await _service.Upload(Upload(Bucket("claude-opus-5", CurrentHour.AddHours(-24), new(1, 0, 0, 0)), Bucket("claude-opus-5", CurrentHour.AddHours(-23), new(2, 0, 0, 0))), Token);
		await _service.Upload(new("pc-2", "Laptop", [Bucket("claude-opus-5", CurrentHour, new(4, 0, 0, 0))]), Token);

		var all = await _service.Get(TokenUsageRange.Last24Hours, null, null, Token);
		var laptop = await _service.Get(TokenUsageRange.Last24Hours, "pc-2", null, Token);

		all.Step.ShouldBe(TokenUsageStep.Hour);
		all.Rows.Select(row => (row.Start, row.Tokens.Input)).ShouldBe([(CurrentHour.AddHours(-23), 2L), (CurrentHour, 4L)]);
		laptop.Rows.ShouldHaveSingleItem().Tokens.Input.ShouldBe(4);
		all.Machines.Select(machine => machine.Name).ShouldBe(["Laptop", "PC bureau"]);
	}

	[Fact]
	public async Task Unpriced_tokens_are_counted_apart_from_the_cost()
	{
		await _service.Upload(Upload(Bucket("gpt-unknown", CurrentHour, new(10, 5, 0, 5))), Token);

		var row = (await _service.Get(TokenUsageRange.Last24Hours, null, null, Token)).Rows.ShouldHaveSingleItem();

		row.CostUsd.ShouldBeNull();
		row.CacheSavingsUsd.ShouldBeNull();
		row.UnpricedTokens.ShouldBe(20);
	}

	[Fact]
	public async Task An_unknown_time_zone_is_a_validation_error()
	{
		await Should.ThrowAsync<RequestValidationException>(() => _service.Get(TokenUsageRange.Last7Days, null, "Mars/Olympus", Token));
	}

	[Fact]
	public async Task An_empty_price_download_keeps_the_known_prices()
	{
		var service = new ModelPriceService(new StaticPriceSource([]), _prices, NullLogger<ModelPriceService>.Instance);

		await Should.ThrowAsync<InvalidOperationException>(() => service.Refresh(Token));

		_prices.Stored.Count.ShouldBe(2);
	}

	private static TokenUsageUpload Upload(params TokenUsageBucketUpload[] buckets)
	{
		return new("pc-1", "PC bureau", buckets);
	}

	private static TokenUsageBucketUpload Bucket(string model, DateTimeOffset hour, TokenCounts tokens)
	{
		return new(Provider.Claude, model, hour, tokens);
	}
}

internal sealed class InMemoryTokenUsage : ITokenUsageRepository
{
	public Dictionary<(string, Provider, string, DateTimeOffset), TokenUsageBucket> Stored { get; } = [];

	public Task Upsert(IReadOnlyList<TokenUsageBucket> buckets, CancellationToken cancellationToken)
	{
		foreach (var bucket in buckets)
			Stored[(bucket.MachineId, bucket.Provider, bucket.Model, bucket.Hour)] = bucket;

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<TokenUsageBucket>> Get(DateTimeOffset from, DateTimeOffset to, string? machineId, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<TokenUsageBucket>>(Stored.Values
			.Where(bucket => bucket.Hour >= from && bucket.Hour < to && (machineId is null || bucket.MachineId == machineId))
			.ToList());
	}
}

internal sealed class InMemoryMachines : IUsageMachineRepository
{
	public Dictionary<string, UsageMachine> Stored { get; } = [];

	public Task Save(UsageMachine machine, CancellationToken cancellationToken)
	{
		Stored[machine.Id] = machine;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<UsageMachine>> GetAll(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<UsageMachine>>(Stored.Values.ToList());
	}
}

internal sealed class InMemoryPrices : IModelPriceRepository
{
	public List<ModelPrice> Stored { get; } = [];

	public Task Save(IReadOnlyList<ModelPrice> prices, CancellationToken cancellationToken)
	{
		Stored.RemoveAll(stored => prices.Any(price => price.Model == stored.Model));
		Stored.AddRange(prices);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<ModelPrice>> Find(IReadOnlyCollection<string> models, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<ModelPrice>>(Stored.Where(price => models.Contains(price.Model)).ToList());
	}

	public Task<bool> Any(CancellationToken cancellationToken)
	{
		return Task.FromResult(Stored.Count > 0);
	}
}

internal sealed class StaticPriceSource(IReadOnlyList<ModelPrice> prices) : IModelPriceSource
{
	public Task<IReadOnlyList<ModelPrice>> Fetch(CancellationToken cancellationToken)
	{
		return Task.FromResult(prices);
	}
}
