using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// One in-flight turn per session, across instances. MUST be called with the ambient unit-of-work
/// transaction already open: the Postgres implementation binds an advisory xact lock to that
/// transaction, auto-released by the server at commit, rollback, or connection death — so a crashed
/// holder can never leave a session locked. Returns null when another turn holds the session —
/// non-blocking, callers surface "busy" instead of queueing.
/// </summary>
public interface ISessionLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>SQLite deployments are single-instance by definition; a process-local set suffices. Singleton.</summary>
public sealed class InMemorySessionLock : ISessionLock
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable?>(_held.TryAdd(sessionId, 0) ? new Lease(this, sessionId) : null);

    private sealed class Lease(InMemorySessionLock owner, Guid sessionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._held.TryRemove(sessionId, out _);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Scoped: must share the request's <see cref="WretchedWhispersDbContext"/> so the lock rides the
/// open unit-of-work transaction.
/// </summary>
public sealed class PostgresSessionLock(WretchedWhispersDbContext db) : ISessionLock
{
    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid sessionId, CancellationToken ct)
    {
        // An xact lock outside a transaction would release instantly and silently guard nothing.
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("ISessionLock requires an open unit-of-work transaction.");

        // ponytail: first 8 bytes of the Guid as the advisory key. A cross-session collision only
        // yields a spurious "busy", never corruption — the Campaign Version token is the backstop.
        var key = BitConverter.ToInt64(sessionId.ToByteArray(), 0);

        var acquired = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync(ct);

        return acquired ? NoopLease.Instance : null;
    }
}
