using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public sealed class TurnQueue(WretchedWhispersDbContext db)
{
    public async Task<(TurnRequestEntity Turn, bool Created)> EnqueueAsync(Guid campaignId, string userId,
        Guid clientRequestId, string message, CancellationToken ct)
    {
        var existing = await db.TurnRequests.SingleOrDefaultAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, ct);
        if (existing is not null)
        {
            if (existing.CampaignId != campaignId || existing.PlayerMessage != message)
                throw new InvalidOperationException("That request ID was already used for a different action.");
            return (existing, false);
        }

        var turn = new TurnRequestEntity { Id = Guid.NewGuid(), CampaignId = campaignId, UserId = userId,
            ClientRequestId = clientRequestId, PlayerMessage = message, CreatedAt = DateTime.UtcNow };
        db.TurnRequests.Add(turn);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var raced = await db.TurnRequests.SingleAsync(x => x.UserId == userId && x.ClientRequestId == clientRequestId, ct);
            if (raced.CampaignId != campaignId || raced.PlayerMessage != message) throw;
            return (raced, false);
        }
        return (turn, true);
    }

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

    public Task CompleteAsync(Guid id, string? error, CancellationToken ct)
    {
        var status = error == null ? "Completed" : "Failed";
        return db.TurnRequests.Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.TerminalError, error).SetProperty(x => x.CompletedAt, DateTime.UtcNow)
            .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), ct);
    }
}
