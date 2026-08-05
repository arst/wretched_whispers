using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Migrations;

public static class Program
{
    public static Task<int> Main() => MigrationRunner.RunAsync(
        Environment.GetEnvironmentVariable("WW_DB_PROVIDER"),
        Environment.GetEnvironmentVariable("WW_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default"),
        Console.Out);
}

public static class MigrationRunner
{
    private const long AdvisoryLockId = 7331952850046564608;

    public static async Task<int> RunAsync(
        string? provider,
        string? connectionString,
        TextWriter log,
        CancellationToken cancellationToken = default)
    {
        provider = provider?.ToLowerInvariant();
        if (provider is not ("postgres" or "sqlite") || string.IsNullOrWhiteSpace(connectionString))
        {
            await log.WriteLineAsync(
                "WW_DB_PROVIDER (postgres|sqlite) and WW_DB_CONNECTION are required.");
            return 2;
        }

        try
        {
            await using var db = CreateContext(provider, connectionString);
            if (provider == "postgres")
                await SetAdvisoryLock(db, locked: true, cancellationToken);

            try
            {
                var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                await log.WriteLineAsync($"Current migration: {applied.LastOrDefault() ?? "<none>"}");
                await log.WriteLineAsync($"Applied migrations: {string.Join(", ", applied)}");
                await log.WriteLineAsync($"Pending migrations: {string.Join(", ", pending)}");
                await db.Database.MigrateAsync(cancellationToken);
                foreach (var migration in pending)
                    await log.WriteLineAsync($"Applied: {migration}");
                return 0;
            }
            finally
            {
                if (provider == "postgres")
                    await SetAdvisoryLock(db, locked: false, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            await log.WriteLineAsync($"Migration failed: {exception.Message}");
            return 1;
        }
    }

    private static WretchedWhispersDbContext CreateContext(string provider, string connectionString) =>
        provider == "postgres"
            ? new PostgresWwDbContext(
                new DbContextOptionsBuilder<PostgresWwDbContext>().UseNpgsql(connectionString).Options)
            : new WretchedWhispersDbContext(
                new DbContextOptionsBuilder<WretchedWhispersDbContext>().UseSqlite(connectionString).Options);

    private static async Task SetAdvisoryLock(
        WretchedWhispersDbContext db, bool locked, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = locked
            ? $"SELECT pg_advisory_lock({AdvisoryLockId})"
            : $"SELECT pg_advisory_unlock({AdvisoryLockId})";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
