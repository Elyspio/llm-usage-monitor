using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     One CLI process at a time per provider: readings, prompts and keep-alive share this lock (refresh tokens are single-use).
/// </summary>
public interface IProviderLocks
{
	/// <summary>
	///     Waits for the lock at most <paramref name="timeout" />; <c>null</c> when it stays held. A bounded wait keeps a stuck
	///     holder from piling up waiting jobs on every Hangfire worker.
	/// </summary>
	Task<IDisposable?> TryAcquire(Provider provider, TimeSpan timeout, CancellationToken cancellationToken);

	bool IsBusy(Provider provider);
}

public sealed class ProviderLocks(TimeProvider time) : IProviderLocks
{
	/// <summary>
	///     The wait of a reading: the next scheduled one covers a skipped reading.
	/// </summary>
	public static readonly TimeSpan PollWait = TimeSpan.FromMinutes(1);

	/// <summary>
	///     The wait of a prompt or a keep-alive: more than a reading and a prompt, both bounded by their CLI timeouts.
	/// </summary>
	public static readonly TimeSpan JobWait = TimeSpan.FromMinutes(5);

	private readonly Dictionary<Provider, SemaphoreSlim> _locks = Enum.GetValues<Provider>().ToDictionary(provider => provider, _ => new SemaphoreSlim(1, 1));

	public async Task<IDisposable?> TryAcquire(Provider provider, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var semaphore = _locks[provider];
		if (semaphore.Wait(0, CancellationToken.None))
		{
			return new Releaser(semaphore);
		}

		using var deadline = new CancellationTokenSource(timeout, time);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		try
		{
			await semaphore.WaitAsync(linked.Token);
			return new Releaser(semaphore);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
	}

	public bool IsBusy(Provider provider)
	{
		return _locks[provider].CurrentCount == 0;
	}

	private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				semaphore.Release();
			}
		}
	}
}
