using System.Globalization;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

public sealed class UsageMonitor(
	IEnumerable<IUsageReader> readers,
	IProviderLocks locks,
	IProviderStateRepository states,
	IUsageSnapshotRepository snapshots,
	IResetRepository resets,
	ISettingsService settingsService,
	IHealthTracker health,
	IClaudeKeepAlive keepAlive,
	IAutomaticTrigger automaticTrigger,
	INotificationService notifications,
	IJobScheduler scheduler,
	TimeProvider time,
	ILogger<UsageMonitor> logger) : IUsageMonitor
{
	private readonly Dictionary<Provider, IUsageReader> _readers = readers.ToDictionary(reader => reader.Provider);

	public async Task Poll(Provider provider, CancellationToken cancellationToken)
	{
		using var providerLock = await locks.Acquire(provider, cancellationToken);

		var now = time.GetUtcNow();
		var state = await states.Get(provider, cancellationToken);
		if (state.BackoffUntil is { } backoffUntil && backoffUntil > now)
		{
			logger.LogInformation("{Provider} reading skipped: rate limited until {BackoffUntil}", provider, backoffUntil);
			return;
		}

		var settings = await settingsService.Get(cancellationToken);
		string? cycleKey;
		try
		{
			if (provider == Provider.Claude)
			{
				state = keepAlive.ScheduleNext(state, await keepAlive.EnsureFresh(cancellationToken));
			}

			var windows = await _readers[provider].Read(cancellationToken);
			if (windows.Count == 0)
			{
				throw new ProviderException(ProviderErrorCodes.NoUsageData, "The provider returned no percentage-based usage windows.");
			}

			var reading = new UsageReading(now, windows);
			await snapshots.Add(provider, reading, cancellationToken);
			await RecordResets(provider, state.LastReading, reading, now, cancellationToken);

			var triggerWindow = reading.TriggerWindow;
			var lastReset = triggerWindow is null ? null : await resets.GetLast(provider, triggerWindow.Id, cancellationToken);
			cycleKey = UsageRules.CycleKey(state, reading, lastReset, now);

			state = await health.RecordSuccess(state, now, cancellationToken) with
			{
				LastReading = reading,
				LastSuccessAt = now,
				CurrentCycleKey = cycleKey
			};
			state = SchedulePostResetCheck(state, reading, settings.Triggers.For(provider).AutoEnabled, now);
			await states.Save(state, cancellationToken);
		}
		catch (ProviderException exception)
		{
			logger.LogWarning("{Provider} reading failed: {Code} {Message}", provider, exception.Code, exception.Message);
			state = await health.RecordFailure(state, exception, now, settings.Notifications.ReadFailureThreshold, cancellationToken);
			await states.Save(state, cancellationToken);
			return;
		}

		if (cycleKey is { } && settings.Triggers.For(provider).AutoEnabled)
			// Still under the provider lock: the prompt never overlaps a reading.
		{
			await automaticTrigger.Run(provider, cycleKey, cancellationToken);
		}
	}

	private async Task RecordResets(Provider provider, UsageReading? previous, UsageReading current, DateTimeOffset now, CancellationToken cancellationToken)
	{
		foreach (var (before, after) in UsageRules.DetectResets(previous, current))
		{
			await resets.Add(provider, after.Id, now, before.UsedPercent, after.UsedPercent, before.ResetsAt, cancellationToken);
			var detail = string.Create(CultureInfo.InvariantCulture, $"{after.Id} : {before.UsedPercent:0.#} % → {after.UsedPercent:0.#} % consommé");
			await notifications.Notify(NotificationKind.Reset, provider, detail, cancellationToken);
		}
	}

	/// <summary>
	///     A reading that sees a reset time ahead on the trigger window schedules a check one minute after it.
	/// </summary>
	private ProviderState SchedulePostResetCheck(ProviderState state, UsageReading reading, bool autoEnabled, DateTimeOffset now)
	{
		if (!autoEnabled || reading.TriggerWindow?.ResetsAt is not { } resetsAt || resetsAt <= now)
		{
			return state;
		}

		var runAt = resetsAt.AddMinutes(1);
		// The provider may shift the announced reset by a few seconds between readings.
		if (state.PendingResetCheck is { } pending && Math.Abs((pending.RunAt - runAt).TotalSeconds) < 60)
		{
			return state;
		}

		if (state.PendingResetCheck is { } previous)
		{
			scheduler.Delete(previous.JobId);
		}

		return state with { PendingResetCheck = new(scheduler.SchedulePostResetCheck(state.Provider, runAt), runAt) };
	}
}