using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     The Claude access token lasts 8 hours and the CLI only refreshes it within five minutes of its expiry: a free CLI
///     command is run in that window. The service never uses the refresh token itself.
/// </summary>
public sealed class ClaudeKeepAlive(
	IClaudeSession session,
	IProviderLocks locks,
	IProviderStateRepository states,
	ISettingsService settingsService,
	IHealthTracker health,
	IJobScheduler scheduler,
	TimeProvider time,
	ILogger<ClaudeKeepAlive> logger) : IClaudeKeepAlive
{
	public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan ScheduleLead = TimeSpan.FromMinutes(4);

	public async Task<ProviderState> EnsureFresh(ProviderState state, CancellationToken cancellationToken)
	{
		return ScheduleNext(state, await Refresh(cancellationToken));
	}

	/// <summary>Refreshes the token if needed and returns its up-to-date expiry. Throws <c>AUTH_EXPIRED</c> when it cannot.</summary>
	private async Task<ClaudeTokenInfo> Refresh(CancellationToken cancellationToken)
	{
		var token = await session.ReadToken(cancellationToken);
		if (token.ExpiresAt is not { } expiresAt || expiresAt - time.GetUtcNow() >= RefreshWindow)
		{
			return token;
		}

		// One retry: the refresh may lose a race with another CLI process that holds the refresh lock.
		for (var attempt = 0; attempt < 2 && token.ExpiresAt <= expiresAt; attempt++)
		{
			logger.LogInformation("Claude token expires at {ExpiresAt}: refreshing through the CLI", expiresAt);
			await session.RefreshThroughCli(cancellationToken);
			token = await session.ReadToken(cancellationToken);
		}

		if (token.ExpiresAt is not { } refreshed || refreshed - time.GetUtcNow() < RefreshWindow)
		{
			throw new ProviderException(ProviderErrorCodes.AuthExpired, "The Claude CLI could not refresh its login. Run `claude auth login` on the service host.");
		}

		return token;
	}

	public async Task Run(CancellationToken cancellationToken)
	{
		using var providerLock = await locks.TryAcquire(Provider.Claude, ProviderLocks.JobWait, cancellationToken);
		if (providerLock is null)
		{
			// The holder is a reading or a prompt: a reading refreshes the token itself.
			logger.LogWarning("Claude keep-alive skipped: the provider is still busy after {Wait}", ProviderLocks.JobWait);
			return;
		}

		var state = await states.Get(Provider.Claude, cancellationToken);
		var settings = await settingsService.Get(cancellationToken);
		try
		{
			state = await EnsureFresh(state, cancellationToken);
			state = await health.CheckCredentialExpiry(state, time.GetUtcNow(), settings.Notifications.CredentialExpiryAlertDays, cancellationToken);
		}
		catch (ProviderException exception)
		{
			state = await health.RecordFailure(state, exception, time.GetUtcNow(), settings.Notifications.ReadFailureThreshold, cancellationToken);
		}

		await states.Save(state, cancellationToken);
	}

	/// <summary>Records the token expiry and schedules the next keep-alive four minutes before it.</summary>
	private ProviderState ScheduleNext(ProviderState state, ClaudeTokenInfo token)
	{
		state = state with { TokenExpiresAt = token.ExpiresAt, RefreshTokenExpiresAt = token.RefreshTokenExpiresAt };
		if (token.ExpiresAt is not { } expiresAt)
		{
			return state;
		}

		var now = time.GetUtcNow();
		var runAt = expiresAt - ScheduleLead;
		if (runAt <= now)
		{
			runAt = now.AddMinutes(1);
		}

		if (state.KeepAlive is { } current && Math.Abs((current.RunAt - runAt).TotalSeconds) < 60)
		{
			return state;
		}

		// A keep-alive whose time has come is running (this very job, maybe) or done: deleting it would abort it.
		if (state.KeepAlive is { } previous && previous.RunAt > now)
		{
			scheduler.Delete(previous.JobId);
		}

		return state with { KeepAlive = new(scheduler.ScheduleKeepAlive(runAt), runAt) };
	}
}