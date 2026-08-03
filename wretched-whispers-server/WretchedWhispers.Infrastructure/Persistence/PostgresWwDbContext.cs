using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// Same model as <see cref="WretchedWhispersDbContext"/>, distinct type so EF can keep a second,
/// Postgres-flavored migration set (Migrations/Postgres) in this same assembly — one assembly can
/// hold two model snapshots only for two context types. Registered AS the
/// <see cref="WretchedWhispersDbContext"/> service when WW_DB_PROVIDER=postgres, so everything
/// downstream (repositories, Identity stores, MigrateAsync) is provider-unaware.
/// </summary>
public sealed class PostgresWwDbContext(DbContextOptions<PostgresWwDbContext> options)
    : WretchedWhispersDbContext(options);

/// <summary>Design-time only (dotnet ef); never connects during `migrations add`.</summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresWwDbContext>
{
    public PostgresWwDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresWwDbContext>()
            .UseNpgsql("Host=localhost;Database=ww-design")
            .Options);
}
