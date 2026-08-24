using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

/// <summary>
/// The (user, client request id) contract. Reusing an id for a different action is a client bug, and
/// it is reported through the result rather than thrown: it used to raise an InvalidOperationException
/// that the endpoint caught alongside every other one and echoed straight back to the caller.
/// </summary>
public class TurnQueueTests : SqliteTestBase
{
    private const string UserId = "test-user";

    [Fact]
    public async Task Enqueue_CreatesOneTurn()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);

        var result = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);

        Assert.True(result.Created);
        Assert.NotNull(result.Turn);
    }

    [Fact]
    public async Task Enqueue_ReplayingTheSameSubmission_ReturnsTheOriginalTurn()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        var campaignId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var first = await queue.EnqueueAsync(
            campaignId, UserId, requestId, "I open the door.", CancellationToken.None);
        var replay = await queue.EnqueueAsync(
            campaignId, UserId, requestId, "I open the door.", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Turn!.Id, replay.Turn!.Id);
    }

    [Theory]
    [InlineData(true, false)]  // same id, different campaign
    [InlineData(false, true)]  // same id, different message
    public async Task Enqueue_ReusingARequestIdForADifferentAction_ReturnsNoTurn(
        bool differentCampaign, bool differentMessage)
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        var campaignId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await queue.EnqueueAsync(campaignId, UserId, requestId, "I open the door.", CancellationToken.None);

        var reused = await queue.EnqueueAsync(
            differentCampaign ? Guid.NewGuid() : campaignId,
            UserId,
            requestId,
            differentMessage ? "I flee instead." : "I open the door.",
            CancellationToken.None);

        Assert.Null(reused.Turn);
        Assert.False(reused.Created);
    }

    [Fact]
    public async Task Lease_RenewAndFinalize_AreFencedByOwner()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        await queue.EnqueueAsync(Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);
        var claimed = await queue.ClaimAsync("worker-a", TimeSpan.FromMinutes(5), 3, CancellationToken.None);
        Assert.NotNull(claimed);

        // Only the lease holder can renew; a worker that reclaimed under a different owner is the
        // only one allowed to decide the outcome.
        Assert.True(await queue.RenewAsync(claimed!.Id, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await queue.RenewAsync(claimed.Id, "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.False(await queue.FinalizeAsync(claimed.Id, "worker-b", null, CancellationToken.None));
        var afterStale = await queue.GetOwnedAsync(claimed.Id, UserId, CancellationToken.None);
        Assert.Equal(TurnStatus.Running, afterStale!.Status);
        Assert.Empty(Db.TurnEvents);

        Assert.True(await queue.FinalizeAsync(claimed.Id, "worker-a", null, CancellationToken.None));
        var afterOwner = await queue.GetOwnedAsync(claimed.Id, UserId, CancellationToken.None);
        Assert.Equal(TurnStatus.Completed, afterOwner!.Status);
        Assert.Equal("done", Assert.Single(Db.TurnEvents).EventType);

        // A second finalize by the same owner is fenced too — the turn is no longer Running, so
        // it reports false instead of attempting (and colliding on) a second terminal event.
        Assert.False(await queue.FinalizeAsync(claimed.Id, "worker-a", null, CancellationToken.None));
        Assert.Single(Db.TurnEvents);
    }

    [Fact]
    public async Task Claim_ExhaustedExpiredLease_FailsTheTurn()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        var queued = await queue.EnqueueAsync(Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);
        var turn = queued.Turn!;
        turn.Status = TurnStatus.Running;
        turn.AttemptCount = 3;
        turn.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await Db.SaveChangesAsync();

        var claimed = await queue.ClaimAsync("worker-b", TimeSpan.FromMinutes(5), 3, CancellationToken.None);

        Assert.Equal(TurnStatus.Failed, claimed!.Status);
        Assert.Equal("Turn exceeded retry limit.", claimed.TerminalError);
        var terminal = Assert.Single(Db.TurnEvents);
        Assert.Equal("error", terminal.EventType);
        Assert.Contains("Turn exceeded retry limit.", terminal.Payload);
    }

    [Fact]
    public async Task Finalize_WithAPreexistingTerminalEvent_CompletesWithoutADuplicate()
    {
        // A pre-atomic writer could crash between appending its terminal event and updating the
        // row; finalization must adopt the existing event instead of colliding with the unique
        // terminal index on every attempt — that turn would head-of-line-block the whole queue.
        var queue = new TurnQueue(Db, TimeProvider.System);
        var queued = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);
        var claimed = await queue.ClaimAsync("worker-a", TimeSpan.FromMinutes(5), 3, CancellationToken.None);
        Assert.NotNull(claimed);

        Db.TurnEvents.Add(new TurnEventEntity
        {
            Id = Guid.NewGuid(),
            TurnId = queued.Turn!.Id,
            Sequence = 1,
            EventType = "error",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        Assert.True(await queue.FinalizeAsync(claimed!.Id, "worker-a", null, CancellationToken.None));

        var turn = await queue.GetOwnedAsync(claimed!.Id, UserId, CancellationToken.None);
        Assert.Equal(TurnStatus.Completed, turn!.Status);
        Assert.Single(Db.TurnEvents.Where(x => x.TurnId == queued.Turn!.Id));
    }

    [Fact]
    public async Task Finalize_WithLeftoverTrackedEntitiesFromAFailedTurn_FlushesOnlyTheTerminalEvent()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        var queued = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);
        var claimed = await queue.ClaimAsync("worker-a", TimeSpan.FromMinutes(5), 3, CancellationToken.None);
        Assert.NotNull(claimed);

        // A failed turn's rolled-back writes stay tracked on the shared scoped context. This
        // pending duplicate of an already-committed (TurnId, Sequence) row can never be saved;
        // finalization must drop it rather than fail on it forever or commit it.
        var otherTurnId = Guid.NewGuid();
        Db.TurnEvents.Add(new TurnEventEntity
        {
            Id = Guid.NewGuid(),
            TurnId = otherTurnId,
            Sequence = 1,
            EventType = "narrative",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
        Db.TurnEvents.Add(new TurnEventEntity
        {
            Id = Guid.NewGuid(),
            TurnId = otherTurnId,
            Sequence = 1,
            EventType = "narrative",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        });

        Assert.True(await queue.FinalizeAsync(claimed!.Id, "worker-a", null, CancellationToken.None));

        var turn = await queue.GetOwnedAsync(claimed!.Id, UserId, CancellationToken.None);
        Assert.Equal(TurnStatus.Completed, turn!.Status);
        Assert.Equal(1, await Db.TurnEvents.CountAsync(x => x.TurnId == otherTurnId));
    }

    [Fact]
    public async Task Enqueue_SameRequestIdFromADifferentUser_IsItsOwnTurn()
    {
        var queue = new TurnQueue(Db, TimeProvider.System);
        var requestId = Guid.NewGuid();

        var mine = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, requestId, "I open the door.", CancellationToken.None);
        var theirs = await queue.EnqueueAsync(
            Guid.NewGuid(), "someone-else", requestId, "I open the door.", CancellationToken.None);

        Assert.True(theirs.Created);
        Assert.NotEqual(mine.Turn!.Id, theirs.Turn!.Id);
    }
}
