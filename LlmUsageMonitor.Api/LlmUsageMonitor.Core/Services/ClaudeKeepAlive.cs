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

	public async Task<ClaudeTokenState> EnsureFresh(CancellationToken cancellationToken)
	{
		var token = await session.ReadToken(cancellationToken);
		if (token.ExpiresAt is not { } expiresAt || expiresAt - time.GetUtcNow() >= RefreshWindow)
		{
			return new(token.ExpiresAt, token.RefreshTokenExpiresAt);
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

		return new(token.ExpiresAt, token.RefreshTokenExpiresAt);
	}

	public async Task Run(CancellationToken cancellationToken)
	{
		using var providerLock = await locks.Acquire(Provider.Claude, cancellationToken);

		var state = await states.Get(Provider.Claude, cancellationToken);
		try
		{
			state = ScheduleNext(state, await EnsureFresh(cancellationToken));
		}
		catch (ProviderException exception)
		{
			var settings = await settingsService.Get(cancellationToken);
			state = await health.RecordFailure(state, exception, time.GetUtcNow(), settings.Notifications.ReadFailureThreshold, cancellationToken);
		}

		await states.Save(state, cancellationToken);
	}

	public ProviderState ScheduleNext(ProviderState state, ClaudeTokenState token)
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

		if (state.KeepAlive is { } previous)
		{
			scheduler.Delete(previous.JobId);
		}

		return state with { KeepAlive = new(scheduler.ScheduleKeepAlive(runAt), runAt) };
	}
}