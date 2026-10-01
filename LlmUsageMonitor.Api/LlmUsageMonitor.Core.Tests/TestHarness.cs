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
		Settings = new(SettingsRepository, States, Scheduler, Protector, appConfig);
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
	public ProviderLocks Locks { get; } = new();

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

	public Task<ProviderState> Get(Provider provider, CancellationToken cancellationToken)
	{
		return Task.FromResult(Stored.TryGetValue(provider, out var state) ? state : new(provider));
	}

	public Task Save(ProviderState state, CancellationToken cancellationToken)
	{
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

	public Task<IReadOnlyList<UsageSeries>> GetHistory(Provider? provider, string? windowId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<UsageSeries>>([]);
	}
}

internal sealed class InMemoryResets : IResetRepository
{
	public List<ResetEvent> Added { get; } = [];

	public Task<ResetEvent> Add(Provider provider, string windowId, DateTimeOffset detectedAt, double usedBefore, double usedAfter, DateTimeOffset? previousResetsAt,
		CancellationToken cancellationToken)
	{
		var reset = new ResetEvent($"reset-{Added.Count + 1}", provider, windowId, detectedAt, usedBefore, usedAfter, previousResetsAt);
		Added.Add(reset);
		return Task.FromResult(reset);
	}

	public Task<ResetEvent?> GetLast(Provider provider, string windowId, CancellationToken cancellationToken)
	{
		return Task.FromResult(Added.LastOrDefault(reset => reset.Provider == provider && reset.WindowId == windowId));
	}
}

internal sealed class InMemoryRuns : ITriggerRunRepository
{
	public List<TriggerRun> All { get; } = [];

	public Task<TriggerRun?> TryStartAutomatic(Provider provider, string cycleKey, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		if (All.Any(run => !run.Manual && run.Provider == provider && run.CycleKey == cycleKey))
		{
			return Task.FromResult<TriggerRun?>(null);
		}

		return Task.FromResult<TriggerRun?>(Start(provider, false, cycleKey, model, startedAt));
	}

	public Task<TriggerRun> StartManual(Provider provider, string model, DateTimeOffset startedAt, CancellationToken cancellationToken)
	{
		return Task.FromResult(Start(provider, true, null, model, startedAt));
	}

	public Task<TriggerRun> Complete(string id, TriggerStatus status, DateTimeOffset endedAt, string? errorCode, string? error, CancellationToken cancellationToken)
	{
		var index = All.FindIndex(run => run.Id == id);
		All[index] = All[index] with { Status = status, EndedAt = endedAt, ErrorCode = errorCode, Error = error };
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

	public Task<long> FailRunning(DateTimeOffset endedAt, string errorCode, string error, CancellationToken cancellationToken)
	{
		return Task.FromResult(0L);
	}

	private TriggerRun Start(Provider provider, bool manual, string? cycleKey, string model, DateTimeOffset startedAt)
	{
		var run = new TriggerRun($"run-{All.Count + 1}", provider, manual, cycleKey, model, TriggerStatus.Running, startedAt, null, null, null);
		All.Add(run);
		return run;
	}
}

internal sealed class InMemorySettings : ISettingsRepository
{
	public AppSettings? Stored { get; set; }

	public Task<AppSettings?> Find(CancellationToken cancellationToken)
	{
		return Task.FromResult(Stored);
	}

	public Task Save(AppSettings settings, CancellationToken cancellationToken)
	{
		Stored = settings;
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
	public ProviderException? Failure { get; set; }

	public List<string> Models { get; } = [];

	public Provider Provider => provider;

	public Task Run(string model, CancellationToken cancellationToken)
	{
		Models.Add(model);
		return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
	}
}

internal sealed class FakeSession : IClaudeSession
{
	public DateTimeOffset? ExpiresAt { get; set; }

	/// <summary>What the CLI does to the token when it runs; nothing by default (the refresh fails).</summary>
	public Func<DateTimeOffset?, DateTimeOffset?> OnRefresh { get; set; } = expiry => expiry;

	public int Refreshes { get; private set; }

	public Task<ClaudeTokenInfo> ReadToken(CancellationToken cancellationToken)
	{
		return Task.FromResult(new ClaudeTokenInfo(ExpiresAt, null));
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
