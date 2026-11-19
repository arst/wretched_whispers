using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>The outcome of an enqueue. A null <see cref="Turn"/> means the client reused a request id
/// for a different campaign or message — a client bug, not a transient fault.</summary>
public readonly record struct TurnEnqueueResult(TurnRequestEntity? Turn, bool Created);

/// <summary>What a cancel request did. Only a turn that had not started is cancelled on the spot; a
/// running one is asked to stop, and a finished one is left alone.</summary>
public enum TurnCancelResult { NotFound, Cancelled, Requested, AlreadyFinished }

public sealed class TurnQueue(WretchedWhispersDbContext db, TimeProvider clock)
{
    /// <summary>The terminal error of a turn the player cancelled.</summary>
    public const string CancelledMessage = "Turn cancelled.";

    /// <summary>
    /// Idempotent on (user, client request id): replaying the same submission returns the original
    /// turn. Reusing that id for a different action is reported through the result rather than an
    /// exception, so no framework exception type doubles as a policy signal at the endpoint.
    /// </summary>
    public async Task<TurnEnqueueResult> EnqueueAsync(Guid campaignId, string userId,
        Guid clientRequestId, string message, CancellationToken ct)
    {
        var existing = await db.TurnRequests.SingleOrDefaultAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, ct);
        if (existing is not null)
            return Replay(existing, campaignId, message);

        var turn = new TurnRequestEntity
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = userId,
            ClientRequestId = clientRequestId,
            PlayerMessage = message,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.TurnRequests.Add(turn);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Lost the race on the (user, request id) index — the winner's row is the answer. Detach
            // ours first or the failed insert stays Added and re-fires on the next SaveChanges.
            db.Entry(turn).State = EntityState.Detached;
            var raced = await db.TurnRequests.SingleOrDefaultAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, ct);
            // No winner row means this wasn't the idempotency race (FK failure, disk full, ...) —
            // surface the real save error instead of burying it under a phantom-row lookup.
            if (raced is null) throw;
            return Replay(raced, campaignId, message);
        }
        return new TurnEnqueueResult(turn, Created: true);
    }

    private static TurnEnqueueResult Replay(TurnRequestEntity existing, Guid campaignId, string message) =>
        existing.CampaignId == campaignId && existing.PlayerMessage == message
            ? new TurnEnqueueResult(existing, Created: false)
            : new TurnEnqueueResult(null, Created: false);

    public Task<TurnRequestEntity?> GetOwnedAsync(Guid id, string userId, CancellationToken ct) =>
        db.TurnRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    public async Task<TurnRequestEntity?> ClaimAsync(string owner, TimeSpan lease, int maxAttempts, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidate = await db.TurnRequests.AsNoTracking().Where(x => x.Status == TurnStatus.Pending || (x.Status == TurnStatus.Running && x.LeaseExpiresAt < now))
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;
        // A cancel requested while the turn waited (requeued, or its owner died) is honoured here,
        // before another attempt is spent on it.
        var cancelled = candidate.CancelRequestedAt is not null;
        if (cancelled || candidate.AttemptCount >= maxAttempts)
        {
            var error = cancelled ? CancelledMessage : "Turn exceeded retry limit.";
            var failed = await CommitTerminalAsync(candidate.Id, error, token =>
                db.TurnRequests.Where(x => x.Id == candidate.Id &&
                        (x.CancelRequestedAt != null || x.AttemptCount >= maxAttempts) &&
                        (x.Status == TurnStatus.Pending || (x.Status == TurnStatus.Running && x.LeaseExpiresAt < now)))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, TurnStatus.Failed)
                        .SetProperty(x => x.TerminalError, error)
                        .SetProperty(x => x.CompletedAt, now), token), ct);
            if (!failed) return null;
            candidate.Status = TurnStatus.Failed;
            candidate.TerminalError = error;
            return candidate;
        }
        var changed = await db.TurnRequests.Where(x => x.Id == candidate.Id && (x.Status == TurnStatus.Pending || (x.Status == TurnStatus.Running && x.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TurnStatus.Running).SetProperty(x => x.LeaseOwner, owner)
                .SetProperty(x => x.LeaseExpiresAt, now.Add(lease)).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        return changed == 1 ? await db.TurnRequests.AsNoTracking().SingleAsync(x => x.Id == candidate.Id, ct) : null;
    }

    /// <summary>Extends a held lease. False means the lease is no longer ours — another worker
    /// reclaimed the turn — and the caller must stop working on it.</summary>
    public async Task<bool> RenewAsync(Guid id, string owner, TimeSpan lease, CancellationToken ct)
    {
        var expires = clock.GetUtcNow().UtcDateTime.Add(lease);
        return await db.TurnRequests.Where(x => x.Id == id && x.LeaseOwner == owner && x.Status == TurnStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, expires), ct) == 1;
    }

    /// <summary>
    /// Hands a running turn back to the queue for another attempt (a transient failure that changed
    /// nothing). Fenced by owner like finalization; the attempt count is kept, so the retry limit
    /// still applies. False means the lease is no longer ours.
    /// </summary>
    public async Task<bool> RequeueAsync(Guid id, string owner, CancellationToken ct) =>
        await db.TurnRequests.Where(x => x.Id == id && x.LeaseOwner == owner && x.Status == TurnStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, TurnStatus.Pending)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), ct) == 1;

    /// <summary>
    /// The player asks to cancel their turn. A turn no worker has started is failed here and now. A
    /// running turn is only MARKED: its lease owner stops the run and finalizes it, so there is still
    /// exactly one decider of a running turn's outcome (a turn that already committed completes).
    /// </summary>
    public async Task<TurnCancelResult> RequestCancelAsync(Guid id, string userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cancelledPending = await CommitTerminalAsync(id, CancelledMessage, token =>
            db.TurnRequests.Where(x => x.Id == id && x.UserId == userId && x.Status == TurnStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, TurnStatus.Failed)
                    .SetProperty(x => x.TerminalError, CancelledMessage)
                    .SetProperty(x => x.CancelRequestedAt, now)
                    .SetProperty(x => x.CompletedAt, now), token), ct);
        if (cancelledPending) return TurnCancelResult.Cancelled;

        var marked = await db.TurnRequests
            .Where(x => x.Id == id && x.UserId == userId && x.Status == TurnStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CancelRequestedAt, x => x.CancelRequestedAt ?? now), ct);
        if (marked == 1) return TurnCancelResult.Requested;

        // Neither pending nor running: finished (possibly in the instant between the two updates —
        // a pending turn that was claimed in that instant is caught by the second update).
        return await db.TurnRequests.AnyAsync(x => x.Id == id && x.UserId == userId, ct)
            ? TurnCancelResult.AlreadyFinished
            : TurnCancelResult.NotFound;
    }

    public Task<bool> IsCancelRequestedAsync(Guid id, CancellationToken ct) =>
        db.TurnRequests.AnyAsync(x => x.Id == id && x.CancelRequestedAt != null, ct);

    /// <summary>Atomically records both sources of terminal truth. The owner predicate fences a
    /// worker that lost its lease; false means the reclaiming worker now decides the outcome.</summary>
    public Task<bool> FinalizeAsync(Guid id, string owner, string? error, CancellationToken ct)
    {
        var status = error == null ? TurnStatus.Completed : TurnStatus.Failed;
        var completedAt = clock.GetUtcNow().UtcDateTime;
        return CommitTerminalAsync(id, error, token =>
            db.TurnRequests.Where(x =>
                    x.Id == id && x.LeaseOwner == owner && x.Status == TurnStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.TerminalError, error)
                    .SetProperty(x => x.CompletedAt, completedAt)
                    .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), token), ct);
    }

    private async Task<bool> CommitTerminalAsync(
        Guid id,
        string? error,
        Func<CancellationToken, Task<int>> transition,
        CancellationToken ct)
    {
        // Finalization must flush exactly the terminal event: a failed turn can leave its rolled-back
        // domain writes tracked on this shared scoped context, and SaveChanges would either re-commit
        // them or fail on them forever.
        db.ChangeTracker.Clear();
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await transition(ct) == 0)
                return false;

            // A terminal event may already exist: a pre-atomic writer crashed between its two
            // commits, or another instance appended one after losing its lease mid-stream. Commit
            // the status transition alone instead of colliding with the unique terminal index forever.
            if (await db.TurnEvents.AnyAsync(x => x.TurnId == id && (x.EventType == "done" || x.EventType == "error"), ct))
            {
                await transaction.CommitAsync(ct);
                return true;
            }

            var terminal = await TurnEventStore.AddNextAsync(db, id,
                error is null ? "done" : "error",
                error is null ? new { } : (object)new { message = error }, clock, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                db.Entry(terminal).State = EntityState.Detached;
            }
        }
    }
}
