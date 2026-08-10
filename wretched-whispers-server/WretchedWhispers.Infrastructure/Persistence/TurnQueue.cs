using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>The outcome of an enqueue. A null <see cref="Turn"/> means the client reused a request id
/// for a different campaign or message — a client bug, not a transient fault.</summary>
public readonly record struct TurnEnqueueResult(TurnRequestEntity? Turn, bool Created);

public sealed class TurnQueue(WretchedWhispersDbContext db, TimeProvider clock)
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
            ClientRequestId = clientRequestId, PlayerMessage = message, CreatedAt = clock.GetUtcNow().UtcDateTime };
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

    /// <summary>The turn transaction always writes its player message, including tool-only turns,
    /// so any message carrying this id is a durable proof that the domain commit succeeded.</summary>
    public Task<bool> WasCommittedAsync(Guid id, CancellationToken ct) =>
        db.ChatMessages.AnyAsync(x => x.TurnId == id, ct);

    public async Task<TurnRequestEntity?> ClaimAsync(string owner, TimeSpan lease, int maxAttempts, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidate = await db.TurnRequests.AsNoTracking().Where(x => x.Status == TurnStatus.Pending || (x.Status == TurnStatus.Running && x.LeaseExpiresAt < now))
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;
        if (candidate.AttemptCount >= maxAttempts)
        {
            const string error = "Turn exceeded retry limit.";
            var failed = await CommitTerminalAsync(candidate.Id, error, token =>
                db.TurnRequests.Where(x => x.Id == candidate.Id && x.AttemptCount >= maxAttempts &&
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
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await transition(ct) == 0)
                return false;

            var sequence = (await db.TurnEvents
                .Where(x => x.TurnId == id)
                .MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
            var terminal = new TurnEventEntity
            {
                Id = Guid.NewGuid(),
                TurnId = id,
                Sequence = sequence,
                EventType = error is null ? "done" : "error",
                Payload = error is null
                    ? "{}"
                    : JsonSerializer.Serialize(new { message = error }, JsonSerializerOptions.Web),
                CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            db.TurnEvents.Add(terminal);

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
