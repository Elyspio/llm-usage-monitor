using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Core.Rules;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class UsageRulesTests
{
	private static readonly DateTimeOffset Now = TestHarness.Start;

	[Fact]
	public void A_drop_of_the_used_share_is_a_reset()
	{
		var previous = new UsageReading(Now.AddMinutes(-3), [Window("five_hour", 40, Now.AddMinutes(-1)), Window("seven_day", 20, Now.AddDays(3), 10_080)]);
		var current = new UsageReading(Now, [Window("five_hour", 0, null), Window("seven_day", 21, Now.AddDays(3), 10_080)]);

		var resets = UsageRules.DetectResets(previous, current);

		resets.ShouldHaveSingleItem().Current.Id.ShouldBe("five_hour");
	}

	[Fact]
	public void No_previous_reading_means_no_reset()
	{
		UsageRules.DetectResets(null, new UsageReading(Now, [Window("five_hour", 0, null)])).ShouldBeEmpty();
	}

	[Fact]
	public void Cycle_is_the_expired_reset_time_truncated_to_the_minute()
	{
		var state = new ProviderState(Provider.Claude) { LastReading = new UsageReading(Now.AddMinutes(-3), [Window("five_hour", 40, new DateTimeOffset(2026, 9, 14, 11, 59, 59, 626, TimeSpan.Zero))]) };

		var key = UsageRules.CycleKey(state, new UsageReading(Now, [Window("five_hour", 0, null)]), null, Now);

		key.ShouldBe("resets:2026-09-14T11:59Z");
	}

	[Fact]
	public void A_window_already_started_or_used_opens_no_cycle()
	{
		var state = new ProviderState(Provider.Claude);

		UsageRules.CycleKey(state, new UsageReading(Now, [Window("five_hour", 0, Now.AddHours(5))]), null, Now).ShouldBeNull();
		UsageRules.CycleKey(state, new UsageReading(Now, [Window("five_hour", 3, null)]), null, Now).ShouldBeNull();
	}

	[Fact]
	public void Without_reset_time_nor_reset_no_cycle_is_known()
	{
		UsageRules.CycleKey(new ProviderState(Provider.Codex), new UsageReading(Now, [Window("codex/primary", 0, null)]), null, Now).ShouldBeNull();
	}

	[Fact]
	public void Without_reset_time_the_last_detected_reset_identifies_the_cycle()
	{
		var reset = new ResetEvent("abc", Provider.Codex, "codex/primary", Now.AddMinutes(-3), 30, 0, null);

		UsageRules.CycleKey(new ProviderState(Provider.Codex), new UsageReading(Now, [Window("codex/primary", 0, null)]), reset, Now).ShouldBe("reset:abc");
	}

	[Fact]
	public void The_recorded_cycle_is_kept_while_the_window_waits_for_its_first_message()
	{
		var state = new ProviderState(Provider.Claude) { CurrentCycleKey = "resets:2026-09-14T11:00Z" };
		var reset = new ResetEvent("later", Provider.Claude, "five_hour", Now, 30, 0, null);

		UsageRules.CycleKey(state, new UsageReading(Now, [Window("five_hour", 0, null)]), reset, Now).ShouldBe("resets:2026-09-14T11:00Z");
	}

	[Fact]
	public void The_trigger_window_is_the_shortest_one()
	{
		var reading = new UsageReading(Now, [Window("seven_day", 10, null, 10_080), Window("five_hour", 5, null), Window("unknown", 0, null, null)]);

		reading.TriggerWindow!.Id.ShouldBe("five_hour");
	}
}
