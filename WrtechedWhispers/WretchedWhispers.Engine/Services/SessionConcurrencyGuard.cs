using System.Collections.Concurrent;

namespace WretchedWhispers.Engine.Services;

public sealed class SessionConcurrencyGuard
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<bool> TryAcquire(Guid sessionId)
    {
        var semaphore = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(TimeSpan.Zero);
    }

    public void Release(Guid sessionId)
    {
        if (_locks.TryGetValue(sessionId, out var semaphore))
            semaphore.Release();
    }
}
