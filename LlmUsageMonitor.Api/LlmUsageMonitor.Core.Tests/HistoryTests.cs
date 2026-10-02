using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class HistoryTests
{
	[Fact]
	public async Task The_history_is_read_by_5_minutes_over_a_day_and_by_hour_over_a_week()
	{
		var harness = new TestHarness();
		var history = new HistoryService(harness.Snapshots, harness.Runs, harness.Time);

		await history.Get(null, null, TimeSpan.FromHours(24), TestContext.Current.CancellationToken);
		harness.Snapshots.LastBucket.ShouldBe(TimeSpan.FromMinutes(5));
		await history.Get(null, null, TimeSpan.FromDays(7), TestContext.Current.CancellationToken);
		harness.Snapshots.LastBucket.ShouldBe(TimeSpan.FromHours(1));
	}
}
