using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public sealed class TurnEventStore(IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task AppendAsync(Guid turnId, string eventType, object payload, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        // Two appenders can race the same next sequence — the unique (TurnId, Sequence) index picks
        // the winner, the loser recomputes and retries.
        for (var attempt = 0; ; attempt++)
        {
            var sequence = (await db.TurnEvents.Where(x => x.TurnId == turnId).MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
            var entity = new TurnEventEntity { Id = Guid.NewGuid(), TurnId = turnId, Sequence = sequence,
                EventType = eventType, Payload = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), CreatedAt = clock.GetUtcNow().UtcDateTime };
            db.TurnEvents.Add(entity);
            try { await db.SaveChangesAsync(ct); return; }
            catch (DbUpdateException) when (attempt < 3)
            {
                db.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    public async Task<List<TurnEventEntity>> ReadAfterAsync(Guid turnId, long sequence, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        return await db.TurnEvents.AsNoTracking().Where(x => x.TurnId == turnId && x.Sequence > sequence)
            .OrderBy(x => x.Sequence).ToListAsync(ct);
    }

    /// <summary>Appends only if the turn has not already ended, so a turn can never carry two endings.</summary>
    public async Task AppendTerminalAsync(Guid turnId, string eventType, object payload, CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
            if (await db.TurnEvents.AnyAsync(
                x => x.TurnId == turnId && (x.EventType == "done" || x.EventType == "error"), ct))
                return;
        }

        await AppendAsync(turnId, eventType, payload, ct);
    }
}
