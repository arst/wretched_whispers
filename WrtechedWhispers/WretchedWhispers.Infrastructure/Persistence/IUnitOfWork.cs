namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// Abstracts the per-turn atomic boundary so callers (e.g. TurnCoordinator) never depend on EF
/// directly. The implementation MUST wrap the same request-scoped DbContext that the turn's tools
/// mutate through — that shared scope is what places those writes inside this transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct);
}

/// <summary>
/// An open transactional scope. <see cref="CommitAsync"/> commits and lets a
/// <c>DbUpdateConcurrencyException</c> propagate to the caller (a UX/policy decision belongs in the
/// caller, not here). Disposal without a prior commit rolls back, best-effort.
/// </summary>
public interface IUnitOfWorkScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
