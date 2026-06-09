using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. Wraps a transaction on the injected
/// (request-scoped) <see cref="WretchedWhispersDbContext"/>, so every repository write made on that
/// same scope during the turn is part of the same transaction.
/// </summary>
public sealed class EfUnitOfWork(WretchedWhispersDbContext dbContext) : IUnitOfWork
{
    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        return new Scope(transaction);
    }

    private sealed class Scope(IDbContextTransaction transaction) : IUnitOfWorkScope
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_committed)
                    await transaction.RollbackAsync();
            }
            catch
            {
                // Best-effort: rollback may fail if the connection is already closed.
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
