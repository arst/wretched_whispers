using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>The outcome of an enqueue. A null <see cref="Turn"/> means the client reused a request id
/// for a different campaign or message — a client bug, not a transient fault.</summary>
public readonly record struct TurnEnqueueResult(TurnRequestEntity? Turn, bool Created);

public sealed class TurnQueue(WretchedWhispersDbContext db)
{
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

        var turn = new TurnRequestEntity { Id = Guid.NewGuid(), CampaignId = campaignId, UserId = userId,
            ClientRequestId = clientRequestId, PlayerMessage = message, CreatedAt = DateTime.UtcNow };
        db.TurnRequests.Add(turn);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Lost the race on the (user, request id) index — the winner's row is the answer. Detach
            // ours first or the failed insert stays Added and re-fires on the next SaveChanges.
            db.Entry(turn).State = EntityState.Detached;
            var raced = await db.TurnRequests.SingleAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, ct);
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
        var now = DateTime.UtcNow;
        var candidate = await db.TurnRequests.AsNoTracking().Where(x => x.Status == "Pending" || (x.Status == "Running" && x.LeaseExpiresAt < now))
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;
        if (candidate.AttemptCount >= maxAttempts)
        {
            await db.TurnRequests.Where(x => x.Id == candidate.Id && x.Status != "Completed").ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Failed").SetProperty(x => x.TerminalError, "Turn exceeded retry limit.")
                .SetProperty(x => x.CompletedAt, now), ct);
            candidate.Status = "Failed";
            candidate.TerminalError = "Turn exceeded retry limit.";
            return candidate;
        }
        var changed = await db.TurnRequests.Where(x => x.Id == candidate.Id && (x.Status == "Pending" || (x.Status == "Running" && x.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Running").SetProperty(x => x.LeaseOwner, owner)
                .SetProperty(x => x.LeaseExpiresAt, now.Add(lease)).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        return changed == 1 ? await db.TurnRequests.AsNoTracking().SingleAsync(x => x.Id == candidate.Id, ct) : null;
    }

    /// <summary>Extends a held lease. False means the lease is no longer ours — another worker
    /// reclaimed the turn — and the caller must stop working on it.</summary>
    public async Task<bool> RenewAsync(Guid id, string owner, TimeSpan lease, CancellationToken ct)
    {
        var expires = DateTime.UtcNow.Add(lease);
        return await db.TurnRequests.Where(x => x.Id == id && x.LeaseOwner == owner && x.Status == "Running")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, expires), ct) == 1;
    }

    /// <summary>Owner-fenced: a worker that lost its lease can no longer decide the turn's outcome —
    /// the reclaiming worker owns it, and its duplicate-answer check reconciles whatever the old
    /// owner managed to commit.</summary>
    public Task CompleteAsync(Guid id, string owner, string? error, CancellationToken ct)
    {
        var status = error == null ? "Completed" : "Failed";
        return db.TurnRequests.Where(x => x.Id == id && x.LeaseOwner == owner)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.TerminalError, error).SetProperty(x => x.CompletedAt, DateTime.UtcNow)
            .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), ct);
    }
}
