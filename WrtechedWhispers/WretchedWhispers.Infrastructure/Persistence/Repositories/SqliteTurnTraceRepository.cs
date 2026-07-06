using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteTurnTraceRepository : ITurnTraceRepository
{
    private readonly WretchedWhispersDbContext _db;

    public SqliteTurnTraceRepository(WretchedWhispersDbContext db)
    {
        _db = db;
    }

    public async Task Save(TurnTraceEntity trace, CancellationToken ct = default)
    {
        trace.OrderIndex = await _db.TurnTraces
            .CountAsync(t => t.ChatSessionId == trace.ChatSessionId, ct);

        _db.TurnTraces.Add(trace);
        await _db.SaveChangesAsync(ct);
    }
}
