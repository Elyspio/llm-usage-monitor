using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class ProviderLockTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_poll_that_cannot_get_the_provider_lock_is_skipped_after_the_wait()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 10, Start.AddHours(1))];
		using var held = await harness.Locks.TryAcquire(Provider.Codex, TimeSpan.Zero, Token);

		var poll = harness.Monitor.Poll(Provider.Codex, Token);
		poll.IsCompleted.ShouldBeFalse();
		harness.Time.Advance(ProviderLocks.PollWait);
		await poll;

		harness.CodexReader.Calls.ShouldBe(0);
		harness.States.Stored.ShouldNotContainKey(Provider.Codex);
	}

	[Fact]
	public async Task A_poll_waiting_for_the_lock_runs_once_it_is_released()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 10, Start.AddHours(1))];
		var held = await harness.Locks.TryAcquire(Provider.Codex, TimeSpan.Zero, Token);

		var poll = harness.Monitor.Poll(Provider.Codex, Token);
		held!.Dispose();
		await poll;

		harness.CodexReader.Calls.ShouldBe(1);
	}

	[Fact]
	public async Task A_manual_trigger_that_cannot_get_the_provider_lock_fails_as_busy()
	{
		var harness = new TestHarness();
		var run = await harness.Triggers.RequestManual(Provider.Claude, Token);
		using var held = await harness.Locks.TryAcquire(Provider.Claude, TimeSpan.Zero, Token);

		var execution = harness.Triggers.ExecuteManual(run.Id, Token);
		harness.Time.Advance(ProviderLocks.JobWait);
		await execution;

		var failed = await harness.Triggers.Get(run.Id, Token);
		failed.Status.ShouldBe(TriggerStatus.Failed);
		failed.ErrorCode.ShouldBe(ProviderErrorCodes.CliBusy);
		harness.ClaudeRunner.Models.ShouldBeEmpty();
	}
}
