using LlmUsageMonitor.Abstractions.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace LlmUsageMonitor.Adapters.MongoDB;

internal sealed class UsageSnapshotDocument
{
	public ObjectId Id { get; set; }
	public DateTime FetchedAt { get; set; }
	public SnapshotMeta Meta { get; set; } = null!;
	public double UsedPercent { get; set; }
	public DateTime? ResetsAt { get; set; }
	public int? WindowDurationMinutes { get; set; }
}

internal sealed class SnapshotMeta
{
	public Provider Provider { get; set; }
	public string WindowId { get; set; } = null!;
}

internal sealed class ResetDocument
{
	public ObjectId Id { get; set; }
	public Provider Provider { get; set; }
	public string WindowId { get; set; } = null!;
	public DateTime DetectedAt { get; set; }
	public double UsedPercentBefore { get; set; }
	public double UsedPercentAfter { get; set; }
	public DateTime? PreviousResetsAt { get; set; }

	public ResetEvent ToDomain()
	{
		return new(Id.ToString(), Provider, WindowId, DetectedAt.ToOffset(), UsedPercentBefore, UsedPercentAfter, PreviousResetsAt.ToOffset());
	}
}

internal sealed class TriggerRunDocument
{
	public ObjectId Id { get; set; }
	public Provider Provider { get; set; }
	public bool Manual { get; set; }

	[BsonIgnoreIfNull] public string? CycleKey { get; set; }

	public string Model { get; set; } = null!;
	public TriggerStatus Status { get; set; }
	public DateTime StartedAt { get; set; }
	public DateTime? EndedAt { get; set; }
	public string? ErrorCode { get; set; }
	public string? Error { get; set; }

	public TriggerRun ToDomain()
	{
		return new(Id.ToString(), Provider, Manual, CycleKey, Model, Status, StartedAt.ToOffset(), EndedAt.ToOffset(), ErrorCode, Error);
	}
}

internal sealed class ProviderStateDocument
{
	[BsonId] public Provider Provider { get; set; }

	public ReadingDocument? LastReading { get; set; }
	public DateTime? LastSuccessAt { get; set; }
	public FailureDocument? LastFailure { get; set; }
	public int ConsecutiveFailures { get; set; }
	public int BackoffLevel { get; set; }
	public DateTime? BackoffUntil { get; set; }
	public List<NotificationKind> ActiveAlerts { get; set; } = [];
	public string? CurrentCycleKey { get; set; }
	public JobDocument? PendingResetCheck { get; set; }
	public JobDocument? KeepAlive { get; set; }
	public DateTime? TokenExpiresAt { get; set; }
	public DateTime? RefreshTokenExpiresAt { get; set; }

	public static ProviderStateDocument FromDomain(ProviderState state)
	{
		return new()
		{
			Provider = state.Provider,
			LastReading = state.LastReading is { } reading ? ReadingDocument.FromDomain(reading) : null,
			LastSuccessAt = state.LastSuccessAt.ToUtc(),
			LastFailure = state.LastFailure is { } failure ? new FailureDocument { Code = failure.Code, Message = failure.Message, At = failure.At.ToUtc() } : null,
			ConsecutiveFailures = state.ConsecutiveFailures,
			BackoffLevel = state.BackoffLevel,
			BackoffUntil = state.BackoffUntil.ToUtc(),
			ActiveAlerts = [.. state.ActiveAlerts],
			CurrentCycleKey = state.CurrentCycleKey,
			PendingResetCheck = JobDocument.FromDomain(state.PendingResetCheck),
			KeepAlive = JobDocument.FromDomain(state.KeepAlive),
			TokenExpiresAt = state.TokenExpiresAt.ToUtc(),
			RefreshTokenExpiresAt = state.RefreshTokenExpiresAt.ToUtc()
		};
	}

	public ProviderState ToDomain()
	{
		return new(Provider)
		{
			LastReading = LastReading?.ToDomain(),
			LastSuccessAt = LastSuccessAt.ToOffset(),
			LastFailure = LastFailure is { } failure ? new ProviderFailure(failure.Code, failure.Message, failure.At.ToOffset()) : null,
			ConsecutiveFailures = ConsecutiveFailures,
			BackoffLevel = BackoffLevel,
			BackoffUntil = BackoffUntil.ToOffset(),
			ActiveAlerts = ActiveAlerts,
			CurrentCycleKey = CurrentCycleKey,
			PendingResetCheck = PendingResetCheck?.ToDomain(),
			KeepAlive = KeepAlive?.ToDomain(),
			TokenExpiresAt = TokenExpiresAt.ToOffset(),
			RefreshTokenExpiresAt = RefreshTokenExpiresAt.ToOffset()
		};
	}
}

internal sealed class ReadingDocument
{
	public DateTime FetchedAt { get; set; }
	public List<WindowDocument> Windows { get; set; } = [];

	public static ReadingDocument FromDomain(UsageReading reading)
	{
		return new()
		{
			FetchedAt = reading.FetchedAt.ToUtc(),
			Windows = reading.Windows.Select(window => new WindowDocument
			{
				WindowId = window.Id,
				UsedPercent = window.UsedPercent,
				ResetsAt = window.ResetsAt.ToUtc(),
				WindowDurationMinutes = window.WindowDurationMinutes
			}).ToList()
		};
	}

	public UsageReading ToDomain()
	{
		return new(
			FetchedAt.ToOffset(),
			Windows.Select(window => new UsageWindow(window.WindowId, window.UsedPercent, window.ResetsAt.ToOffset(), window.WindowDurationMinutes)).ToList());
	}
}

internal sealed class WindowDocument
{
	// Not "Id": the driver maps such a member to _id.
	public string WindowId { get; set; } = null!;
	public double UsedPercent { get; set; }
	public DateTime? ResetsAt { get; set; }
	public int? WindowDurationMinutes { get; set; }
}

internal sealed class FailureDocument
{
	public string Code { get; set; } = null!;
	public string Message { get; set; } = null!;
	public DateTime At { get; set; }
}

internal sealed class JobDocument
{
	public string JobId { get; set; } = null!;
	public DateTime RunAt { get; set; }

	public static JobDocument? FromDomain(ScheduledJob? job)
	{
		return job is null ? null : new JobDocument { JobId = job.JobId, RunAt = job.RunAt.ToUtc() };
	}

	public ScheduledJob ToDomain()
	{
		return new(JobId, RunAt.ToOffset());
	}
}

internal sealed class SettingsDocument
{
	public const string GlobalId = "global";

	public string Id { get; set; } = GlobalId;
	public int ClaudeIntervalMinutes { get; set; }
	public int CodexIntervalMinutes { get; set; }
	public ProviderTriggerDocument ClaudeTrigger { get; set; } = null!;
	public ProviderTriggerDocument CodexTrigger { get; set; } = null!;
	public NotificationsDocument Notifications { get; set; } = null!;

	public static SettingsDocument FromDomain(AppSettings settings)
	{
		return new()
		{
			ClaudeIntervalMinutes = settings.Polling.ClaudeIntervalMinutes,
			CodexIntervalMinutes = settings.Polling.CodexIntervalMinutes,
			ClaudeTrigger = ProviderTriggerDocument.FromDomain(settings.Triggers.Claude),
			CodexTrigger = ProviderTriggerDocument.FromDomain(settings.Triggers.Codex),
			Notifications = NotificationsDocument.FromDomain(settings.Notifications)
		};
	}

	public AppSettings ToDomain()
	{
		return new(
			new(ClaudeIntervalMinutes, CodexIntervalMinutes),
			new(ClaudeTrigger.ToDomain(), CodexTrigger.ToDomain()),
			Notifications.ToDomain());
	}
}

internal sealed class ProviderTriggerDocument
{
	public bool AutoEnabled { get; set; }
	public string Model { get; set; } = null!;

	public static ProviderTriggerDocument FromDomain(ProviderTriggerSettings settings)
	{
		return new() { AutoEnabled = settings.AutoEnabled, Model = settings.Model };
	}

	public ProviderTriggerSettings ToDomain()
	{
		return new(AutoEnabled, Model);
	}
}

internal sealed class NotificationsDocument
{
	public string Url { get; set; } = null!;
	public string? Topic { get; set; }
	public string? ProtectedToken { get; set; }
	/// <summary>Flat event settings written before provider-specific notification settings existed.</summary>
	[BsonElement("events"), BsonIgnoreIfNull]
	public NotificationEvents? LegacyEvents { get; set; }
	[BsonIgnoreIfNull] public NotificationEventsByProvider? ProviderEvents { get; set; }
	public int ReadFailureThreshold { get; set; }
	public FailureDocument? LastSendFailure { get; set; }

	public static NotificationsDocument FromDomain(NotificationSettings settings)
	{
		return new()
		{
			Url = settings.Url,
			Topic = settings.Topic,
			ProtectedToken = settings.ProtectedToken,
			ProviderEvents = settings.Events,
			ReadFailureThreshold = settings.ReadFailureThreshold,
			LastSendFailure = settings.LastSendFailure is { } failure ? new FailureDocument { Code = "SEND_FAILED", Message = failure.Message, At = failure.At.ToUtc() } : null
		};
	}

	public NotificationSettings ToDomain()
	{
		return new(
			Url,
			Topic,
			ProtectedToken,
			ProviderEvents ?? new(LegacyEvents ?? NotificationEvents.Default, LegacyEvents ?? NotificationEvents.Default),
			ReadFailureThreshold,
			LastSendFailure is { } failure ? new NotificationSendFailure(failure.At.ToOffset(), failure.Message) : null);
	}
}

internal sealed class DataProtectionKeyDocument
{
	public ObjectId Id { get; set; }
	public string? FriendlyName { get; set; }
	public string Xml { get; set; } = null!;
}
