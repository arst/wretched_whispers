using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence;

public interface ITurnTraceRepository
{
    /// <summary>Appends a turn trace, assigning its per-session OrderIndex. Participates in the caller's
    /// open transaction (same scoped DbContext).</summary>
    Task Save(TurnTraceEntity trace, CancellationToken ct = default);
}
