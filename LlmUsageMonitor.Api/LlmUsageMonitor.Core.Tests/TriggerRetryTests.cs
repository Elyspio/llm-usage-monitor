using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Core.Services;
using Shouldly;
using Xunit;
using static LlmUsageMonitor.Core.Tests.TestHarness;

namespace LlmUsageMonitor.Core.Tests;

public sealed class TriggerRetryTests
{
	private static readonly DateTimeOffset ResetsAt = Start.AddMinutes(10);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>A Codex window used until <see cref="ResetsAt" />, then back to 0 % and waiting for its first message.</summary>
	private static async Task<TestHarness> WaitingCycle()
	{
		var harness = new TestHarness();
		harness.CodexReader.Respond = () => [Window("codex/primary", 40, ResetsAt)];
		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.SetUtcNow(ResetsAt.AddMinutes(1));
		harness.CodexReader.Respond = () => [Window("codex/primary", 0, null)];
		return harness;
	}

	[Fact]
	public async Task A_transient_failure_is_retried_after_2_5_then_10_minutes_then_notified_once()
	{
		var harness = await WaitingCycle();
		harness.CodexRunner.Failure = new ProviderException(ProviderErrorCode.Overloaded, "529 Overloaded");

		await harness.Monitor.Poll(Provider.Codex, Token);
		foreach (var delay in TriggerService.RetryDelays)
		{
			// A reading before the retry is due does not prompt.
			await harness.Monitor.Poll(Provider.Codex, Token);
			harness.Time.Advance(delay);
			await harness.Monitor.Poll(Provider.Codex, Token);
		}

		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.Advance(TimeSpan.FromHours(1));
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.CodexRunner.Models.Count.ShouldBe(TriggerService.MaxAttempts);
		var run = harness.Runs.All.ShouldHaveSingleItem();
		(run.Status, run.Attempts, run.NextRetryAt).ShouldBe((TriggerStatus.Failed, 4, null));
		var retried = ResetsAt.AddMinutes(1);
		harness.Scheduler.TriggerRetries.Select(retry => retry.RunAt).ShouldBe([retried.AddMinutes(2), retried.AddMinutes(7), retried.AddMinutes(17)]);
		harness.Sender.Sent.Count(message => message.Title == "Codex : déclenchement échoué").ShouldBe(1);
	}

	[Fact]
	public async Task A_retry_that_succeeds_opens_the_cycle()
	{
		var harness = await WaitingCycle();
		harness.CodexRunner.Outcomes.Enqueue(new ProviderException(ProviderErrorCode.Timeout, "Codex prompt timed out."));

		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.Advance(TriggerService.RetryDelays[0]);
		await harness.Monitor.Poll(Provider.Codex, Token);

		var run = harness.Runs.All.ShouldHaveSingleItem();
		(run.Status, run.Attempts).ShouldBe((TriggerStatus.Succeeded, 2));
		harness.Sender.Sent.Select(message => message.Title).ShouldContain("Codex : nouveau cycle ouvert");
		harness.Sender.Sent.Select(message => message.Title).ShouldNotContain("Codex : déclenchement échoué");
	}

	[Theory]
	[InlineData(ProviderErrorCode.AuthExpired)]
	[InlineData(ProviderErrorCode.UsageLimit)]
	[InlineData(ProviderErrorCode.TriggerFailed)]
	public async Task A_failure_that_is_not_transient_is_never_retried(ProviderErrorCode code)
	{
		var harness = await WaitingCycle();
		harness.CodexRunner.Failure = new ProviderException(code, "no");

		await harness.Monitor.Poll(Provider.Codex, Token);
		harness.Time.Advance(TimeSpan.FromMinutes(30));
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.CodexRunner.Models.Count.ShouldBe(1);
		harness.Scheduler.TriggerRetries.ShouldBeEmpty();
		harness.Runs.All.ShouldHaveSingleItem().NextRetryAt.ShouldBeNull();
	}

	[Fact]
	public async Task A_retry_is_bounded_to_its_cycle()
	{
		var harness = await WaitingCycle();
		harness.CodexRunner.Failure = new ProviderException(ProviderErrorCode.CliExited, "Codex exited before completing the turn.");
		await harness.Monitor.Poll(Provider.Codex, Token);

		// The window started meanwhile (manual prompt, use from another device): the cycle no longer waits.
		harness.CodexReader.Respond = () => [Window("codex/primary", 3, ResetsAt.AddHours(5))];
		harness.Time.Advance(TriggerService.RetryDelays[0]);
		await harness.Monitor.Poll(Provider.Codex, Token);

		harness.CodexRunner.Models.Count.ShouldBe(1);
	}

	[Fact]
	public async Task A_run_interrupted_by_a_restart_is_retried_by_the_next_reading()
	{
		var harness = await WaitingCycle();
		var cycleKey = $"resets:{ResetsAt:yyyy-MM-ddTHH:mm}Z";
		harness.Runs.Start(Provider.Codex, false, cycleKey, "gpt-5.6-luna", ResetsAt);
		await harness.States.Save(harness.States.Stored[Provider.Codex] with { CurrentCycleKey = cycleKey }, Token);

		(await harness.Triggers.RecoverInterrupted(Token)).ShouldBe(1);
		harness.Runs.All.ShouldHaveSingleItem().ErrorCode.ShouldBe(ProviderErrorCode.Interrupted.ToStoredCode());
		await harness.Monitor.Poll(Provider.Codex, Token);

		var run = harness.Runs.All.ShouldHaveSingleItem();
		(run.Status, run.Attempts).ShouldBe((TriggerStatus.Succeeded, 2));
	}

	[Fact]
	public async Task An_interrupted_manual_run_is_failed_and_never_retried()
	{
		var harness = new TestHarness();
		var run = await harness.Triggers.RequestManual(Provider.Claude, Token);

		await harness.Triggers.RecoverInterrupted(Token);

		var failed = await harness.Triggers.Get(run.Id, Token);
		(failed.Status, failed.ErrorCode, failed.NextRetryAt).ShouldBe((TriggerStatus.Failed, ProviderErrorCode.Interrupted.ToStoredCode(), null));
	}

	[Fact]
	public async Task A_cancellation_ends_the_run_instead_of_leaving_it_running()
	{
		var harness = new TestHarness();
		harness.ClaudeRunner.Failure = new OperationCanceledException();
		var run = await harness.Triggers.RequestManual(Provider.Claude, Token);

		await Should.ThrowAsync<OperationCanceledException>(() => harness.Triggers.ExecuteManual(run.Id, Token));

		var ended = await harness.Triggers.Get(run.Id, Token);
		(ended.Status, ended.ErrorCode).ShouldBe((TriggerStatus.Failed, ProviderErrorCode.Cancelled.ToStoredCode()));
		(await harness.Runs.GetRunning(Provider.Claude, Token)).ShouldBeNull();
	}

	[Fact]
	public async Task Two_simultaneous_manual_requests_start_a_single_prompt()
	{
		var harness = new TestHarness();
		var release = new TaskCompletionSource();
		harness.Runs.BeforeStartManual = () => release.Task;

		var first = harness.Triggers.RequestManual(Provider.Codex, Token);
		var second = harness.Triggers.RequestManual(Provider.Codex, Token);
		release.SetResult();
		var outcomes = await Task.WhenAll(Capture(first), Capture(second));

		outcomes.Count(outcome => outcome is null).ShouldBe(1);
		outcomes.OfType<ProviderException>().ShouldHaveSingleItem().Code.ShouldBe(ProviderErrorCode.CliBusy);
		harness.Scheduler.EnqueuedTriggers.ShouldHaveSingleItem();
		harness.Runs.All.ShouldHaveSingleItem();
	}

	private static async Task<Exception?> Capture(Task task)
	{
		try
		{
			await task;
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}
}
