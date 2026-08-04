using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public class InMemorySessionLockTests
{
    private readonly InMemorySessionLock _lock = new();

    [Fact]
    public async Task TryAcquire_ReturnsLease_OnFirstCall()
    {
        var lease = await _lock.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(lease);
    }

    [Fact]
    public async Task TryAcquire_ReturnsNull_OnSecondCallSameSession()
    {
        var sessionId = Guid.NewGuid();

        await _lock.TryAcquireAsync(sessionId, CancellationToken.None);
        var second = await _lock.TryAcquireAsync(sessionId, CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquire_ReturnsLease_AfterLeaseDisposed()
    {
        var sessionId = Guid.NewGuid();

        var first = await _lock.TryAcquireAsync(sessionId, CancellationToken.None);
        Assert.NotNull(first);
        await first.DisposeAsync();
        var second = await _lock.TryAcquireAsync(sessionId, CancellationToken.None);

        Assert.NotNull(second);
    }

    [Fact]
    public async Task TryAcquire_DifferentSessions_DoNotInterfere()
    {
        var leaseA = await _lock.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);
        var leaseB = await _lock.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(leaseA);
        Assert.NotNull(leaseB);
    }
}
