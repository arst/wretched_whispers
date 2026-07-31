using System.Collections.Concurrent;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// One in-flight turn per session. Acquisition is non-blocking (a busy session 409s, it never
/// queues), so a set of held session ids is all the state needed — releasing removes the entry,
/// which also keeps the dictionary from growing with every session ever played.
/// </summary>
public sealed class SessionConcurrencyGuard
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    public bool TryAcquire(Guid sessionId) => _held.TryAdd(sessionId, 0);

    public void Release(Guid sessionId) => _held.TryRemove(sessionId, out _);
}
