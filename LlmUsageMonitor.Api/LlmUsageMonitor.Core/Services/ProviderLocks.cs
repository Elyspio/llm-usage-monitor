using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Core.Services;

/// <summary>
///     One CLI process at a time per provider: readings, prompts and keep-alive share this lock (refresh tokens are single-use).
/// </summary>
public interface IProviderLocks
{
	Task<IDisposable> Acquire(Provider provider, CancellationToken cancellationToken);

	bool IsBusy(Provider provider);
}

public sealed class ProviderLocks : IProviderLocks
{
	private readonly Dictionary<Provider, SemaphoreSlim> _locks = Enum.GetValues<Provider>().ToDictionary(provider => provider, _ => new SemaphoreSlim(1, 1));

	public async Task<IDisposable> Acquire(Provider provider, CancellationToken cancellationToken)
	{
		var semaphore = _locks[provider];
		await semaphore.WaitAsync(cancellationToken);
		return new Releaser(semaphore);
	}

	public bool IsBusy(Provider provider) => _locks[provider].CurrentCount == 0;

	private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
		}
	}
}
