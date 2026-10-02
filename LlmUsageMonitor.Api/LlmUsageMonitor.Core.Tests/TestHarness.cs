using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LlmUsageMonitor.Core.Tests;

/// <summary>
///     The real application services wired to in-memory storage, fake adapters and a controllable clock.
/// </summary>
internal sealed class TestHarness
{
	public static readonly DateTimeOffset Start = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

	public TestHarness(bool autoTriggerEnabled = true)
	{
		Locks = new(Time);
		var defaults = AppSettings.CreateDefault(autoTriggerEnabled);
		SettingsRepository.Stored = defaults with
		{
			Notifications = defaults.Notifications with
			{
				Topic = "tests",
				Events = new(new(true, true, true, true, true, true), new(true, true, true, true, true, true))
			}
		};

		var appConfig = Options.Create(new AppConfig { PublicUrl = "https://monitor.test", AutoTriggerEnabledByDefault = autoTriggerEnabled });
		Settings = new(SettingsRepository, States, Scheduler, Protector, Locks, appConfig, NullLogger<SettingsService>.Instance);
		Notifications = new(Sender, SettingsRepository, Settings, Protector, appConfig, Time, NullLogger<NotificationService>.Instance);
		Health = new(Notifications);
		Triggers = new([ClaudeRunner, CodexRunner], Runs, Locks, Settings, Notifications, Scheduler, Time, NullLogger<TriggerService>.Instance);
		KeepAlive = new(Session, Locks, States, Settings, Health, Scheduler, Time, NullLogger<ClaudeKeepAlive>.Instance);
		Monitor = new([ClaudeReader, CodexReader], Locks, States, Snapshots, Resets, Settings, Health, KeepAlive, Triggers, Notifications, Scheduler, Time, NullLogger<UsageMonitor>.Instance);
		Session.ExpiresAt = Start.AddHours(8);
	}

	public FakeTimeProvider Time { get; } = new(Start);
	public InMemoryStates States { get; } = new();
	public InMemorySnapshots Snapshots { get; } = new();
	public InMemoryResets Resets { get; } = new();
	public InMemoryRuns Runs { get; } = new();
	public InMemorySettings SettingsRepository { get; } = new();
	public FakeScheduler Scheduler { get; } = new();
	public FakeReader ClaudeReader { get; } = new(Provider.Claude);
	public FakeReader CodexReader { get; } = new(Provider.Codex);
	public FakeRunner ClaudeRunner { get; } = new(Provider.Claude);
	public FakeRunner CodexRunner { get; } = new(Provider.Codex);
	public FakeSession Session { get; } = new();
	public RecordingSender Sender { get; } = new();
	public ReversibleProtector Protector { get; } = new();
	public ProviderLocks Locks { get; }

	public SettingsService Settings { get; }
	public NotificationService Notifications { get; }
	public HealthTracker Health { get; }
	public TriggerService Triggers { get; }
	public ClaudeKeepAlive KeepAlive { get; }
	public UsageMonitor Monitor { get; }

	public static UsageWindow Window(string id, double used, DateTimeOffset? resetsAt, int? duration = 300)
	{
		return new(id, used, resetsAt, duration);
	}
}

internal sealed class InMemoryStates : IProviderStateRepository
{
	public Dictionary<Provider, ProviderState> Stored { get; } = [];

	/// <summary>The next saves that fail, as a storage outage would.</summary>
	public int FailingSaves { get; set; }

	public Task<ProviderState> Get(Provider provider, CancellationToken cancellationToken)
	{
		return Task.FromResult(Stored.TryGetValue(provider, out var state) ? state : new(provider));
	}

	public Task Save(ProviderState state, CancellationToken cancellationToken)
	{
		if (FailingSaves > 0)
		{
			FailingSaves--;
			return Task.FromException(new TimeoutException("A timeout occurred after 30000ms selecting a server."));
		}

		Stored[state.Provider] = state;
		return Task.CompletedTask;
	}
}

internal sealed class InMemorySnapshots : IUsageSnapshotRepository
{
	public List<(Provider Provider, UsageReading Reading)> Added { get; } = [];

	public Task Add(Provider provider, UsageReading reading, CancellationToken cancellationToken)
	{
		Added.Add((provider, reading));
		return Task.CompletedTask;
	}

	public TimeSpan? LastBucket { get; private set; }

	public Task<IReadOnlyList<UsageSeries>> GetHistory(Provider? provider, string? windowId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken cancellationToken)
	{
		LastBucket = bucket;
		return Task.FromResult<IReadOnlyList<UsageSeries>>([]);
	}
}

internal sealed class InMemoryResets : IResetRepository
{
	private readonly HashSet<(Provider, string, DateTimeOffset)> _keys = [];

	public List<ResetEvent> Added { get; } = [];

	public Task<ResetEvent?> TryAdd(Provider provider, string windowId, DateTimeOffset previousFetchedAt, DateTimeOffset detectedAt, double usedBefore, double usedAfter,
		DateTimeOffset? previousResetsAt, CancellationToken cancellationToken)
	{
		if (!_keys.Add((provider, windowId, previousFetchedAt)))
		{
			return Task.FromResult<ResetEvent?>(null);
		}

		var reset = new ResetEvent($"reset-{Added.Count + 1}", provider, windowId, detectedAt, usedBefore, usedAfter, previousResetsAt);
		Added.Add(reset);
		return Task.FromResult<ResetEvent?>(reset);
	}

	public Task<ResetEvent?> GetLast(Provider provider, string windowId, CancellationToken cancellationToken)
	{
		return Task.FromResult(Added.LastOrDefault(reset => reset.Provider == provider && reset.WindowId == windowId));
	}
}

internal sealed class InMemoryRuns : ITriggerRunRepository
{
	public List<TriggerRun> All { get; } = [];

	/// <summary>Awaited before a manual run is stored, to hold concurrent requests between their check and their insert.</summary>
	public Func<Task>? BeforeStartManual { get; set; }

	public Task<TriggerRun?> TryStartAutomatic(Provider provider, string cycleKey, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		var index = All.FindIndex(run => !run.Manual && run.Provider == provider && run.CycleKey == cycleKey);
		if (index < 0)
		{
			return Task.FromResult<TriggerRun?>(Start(provider, false, cycleKey, model, startedAt));
		}

		var existing = All[index];
		if (existing.Status != TriggerStatus.Failed || existing.NextRetryAt is not { } retryAt || retryAt > startedAt)
		{
			return Task.FromResult<TriggerRun?>(null);
		}

		All[index] = existing with
		{
			Status = TriggerStatus.Running, Model = model, StartedAt = startedAt, EndedAt = null, ErrorCode = null, Error = null, NextRetryAt = null, Attempts = existing.Attempts + 1
		};
		return Task.FromResult<TriggerRun?>(All[index]);
	}

	public async Task<TriggerRun> StartManual(Provider provider, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		if (BeforeStartManual is { } before)
		{
			await before();
		}

		return Start(provider, true, null, model, startedAt);
	}

	public Task<TriggerRun> Complete(string id, TriggerStatus status, DateTimeOffset endedAt, string? errorCode, string? error, DateTimeOffset? nextRetryAt,
		CancellationToken cancellationToken)
	{
		var index = All.FindIndex(run => run.Id == id);
		All[index] = All[index] with { Status = status, EndedAt = endedAt, ErrorCode = errorCode, Error = error, NextRetryAt = nextRetryAt };
		return Task.FromResult(All[index]);
	}

	public Task<TriggerRun?> Get(string id, CancellationToken cancellationToken)
	{
		return Task.FromResult(All.FirstOrDefault(run => run.Id == id));
	}

	public Task<TriggerRun?> GetRunning(Provider provider, CancellationToken cancellationToken)
	{
		return Task.FromResult(All.LastOrDefault(run => run.Provider == provider && run.Status == TriggerStatus.Running));
	}

	public Task<IReadOnlyList<TriggerRun>> GetRecent(int count, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<TriggerRun>>(All.OrderByDescending(run => run.StartedAt).Take(count).ToList());
	}

	public Task<IReadOnlyList<TriggerRun>> GetBetween(Provider? provider, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<TriggerRun>>(All.Where(run => provider is null || run.Provider == provider).ToList());
	}

	public Task<IReadOnlyList<TriggerRun>> GetAllRunning(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<TriggerRun>>(All.Where(run => run.Status == TriggerStatus.Running).ToList());
	}

	public TriggerRun Start(Provider provider, bool manual, string? cycleKey, string model, DateTimeOffset startedAt)
	{
		var run = new TriggerRun($"run-{All.Count + 1}", provider, manual, cycleKey, model, TriggerStatus.Running, startedAt, null, null, null, 1, null);
		All.Add(run);
		return run;
	}
}

internal sealed class InMemorySettings : ISettingsRepository
{
	public AppSettings? Stored { get; set; }

	/// <summary>Runs once after the next read, before its result is returned: a write racing with a read-modify-write.</summary>
	public Func<Task>? AfterNextFind { get; set; }

	public async Task<AppSettings?> Find(CancellationToken cancellationToken)
	{
		var found = Stored;
		if (AfterNextFind is { } hook)
		{
			AfterNextFind = null;
			await hook();
		}

		return found;
	}

	public Task Initialize(AppSettings defaults, CancellationToken cancellationToken)
	{
		Stored ??= defaults;
		return Task.CompletedTask;
	}

	public Task SavePolling(PollingSettings polling, CancellationToken cancellationToken)
	{
		Stored = Stored! with { Polling = polling };
		return Task.CompletedTask;
	}

	public Task SaveTriggers(TriggerSettings triggers, CancellationToken cancellationToken)
	{
		Stored = Stored! with { Triggers = triggers };
		return Task.CompletedTask;
	}

	public Task SaveNotifications(NotificationSettings notifications, CancellationToken cancellationToken)
	{
		Stored = Stored! with { Notifications = notifications with { LastSendFailure = Stored.Notifications.LastSendFailure } };
		return Task.CompletedTask;
	}

	public Task SaveSendFailure(NotificationSendFailure? failure, CancellationToken cancellationToken)
	{
		Stored = Stored! with { Notifications = Stored.Notifications with { LastSendFailure = failure } };
		return Task.CompletedTask;
	}
}

internal sealed class FakeScheduler : IJobScheduler
{
	private int _nextId;

	public Dictionary<Provider, int> PollIntervals { get; } = [];
	public List<Provider> EnqueuedPolls { get; } = [];
	public List<(string JobId, Provider Provider, DateTimeOffset RunAt)> PostResetChecks { get; } = [];
	public List<(string JobId, DateTimeOffset RunAt)> KeepAlives { get; } = [];
	public List<(Provider Provider, DateTimeOffset RunAt)> TriggerRetries { get; } = [];
	public List<string> EnqueuedTriggers { get; } = [];
	public List<string> Deleted { get; } = [];

	public void SetPollInterval(Provider provider, int minutes)
	{
		PollIntervals[provider] = minutes;
	}

	public void EnqueuePoll(Provider provider)
	{
		EnqueuedPolls.Add(provider);
	}

	public string SchedulePostResetCheck(Provider provider, DateTimeOffset runAt)
	{
		var id = $"job-{++_nextId}";
		PostResetChecks.Add((id, provider, runAt));
		return id;
	}

	public string ScheduleTriggerRetry(Provider provider, DateTimeOffset runAt)
	{
		TriggerRetries.Add((provider, runAt));
		return $"job-{++_nextId}";
	}

	public string ScheduleKeepAlive(DateTimeOffset runAt)
	{
		var id = $"job-{++_nextId}";
		KeepAlives.Add((id, runAt));
		return id;
	}

	public void EnqueueTrigger(string runId)
	{
		EnqueuedTriggers.Add(runId);
	}

	public void SchedulePriceRefresh()
	{
	}

	public void ScheduleJobPurge()
	{
	}

	public void EnqueuePriceRefresh()
	{
	}

	public void Delete(string jobId)
	{
		Deleted.Add(jobId);
	}
}

internal sealed class FakeReader(Provider provider) : IUsageReader
{
	public Func<IReadOnlyList<UsageWindow>> Respond { get; set; } = () => [];

	public int Calls { get; private set; }

	public Provider Provider => provider;

	public Task<IReadOnlyList<UsageWindow>> Read(CancellationToken cancellationToken)
	{
		Calls++;
		return Task.FromResult(Respond());
	}
}

internal sealed class FakeRunner(Provider provider) : IPromptRunner
{
	public Exception? Failure { get; set; }

	/// <summary>Failures of the next runs, in order; <see cref="Failure" /> applies once they are used up.</summary>
	public Queue<Exception?> Outcomes { get; } = new();

	public List<string> Models { get; } = [];

	public Provider Provider => provider;

	public Task Run(string model, CancellationToken cancellationToken)
	{
		Models.Add(model);
		var failure = Outcomes.TryDequeue(out var next) ? next : Failure;
		return failure is null ? Task.CompletedTask : Task.FromException(failure);
	}
}

internal sealed class FakeSession : IClaudeSession
{
	public DateTimeOffset? ExpiresAt { get; set; }

	public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

	/// <summary>What the CLI does to the token when it runs; nothing by default (the refresh fails).</summary>
	public Func<DateTimeOffset?, DateTimeOffset?> OnRefresh { get; set; } = expiry => expiry;

	public int Refreshes { get; private set; }

	public Task<ClaudeTokenInfo> ReadToken(CancellationToken cancellationToken)
	{
		return Task.FromResult(new ClaudeTokenInfo(ExpiresAt, RefreshTokenExpiresAt));
	}

	public Task RefreshThroughCli(CancellationToken cancellationToken)
	{
		Refreshes++;
		ExpiresAt = OnRefresh(ExpiresAt);
		return Task.CompletedTask;
	}
}

internal sealed class RecordingSender : INotificationSender
{
	public List<NotificationMessage> Sent { get; } = [];

	public Exception? Failure { get; set; }

	public Task Send(NotificationMessage message, string serverUrl, string topic, string? token, CancellationToken cancellationToken)
	{
		if (Failure is { })
		{
			return Task.FromException(Failure);
		}

		Sent.Add(message);
		return Task.CompletedTask;
	}
}

internal sealed class ReversibleProtector : ISecretProtector
{
	public string Protect(string value)
	{
		return $"protected:{value}";
	}

	public string Unprotect(string value)
	{
		return value["protected:".Length..];
	}
}
