using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Health;

/// <summary>
/// Readiness: the database answers, and the schema the running code expects is actually applied.
/// Health checks are resolved from a fresh scope per probe, so the DbContext is injected directly
/// rather than through a scope factory.
/// </summary>
public sealed class DatabaseHealthCheck(WretchedWhispersDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Database is unavailable");

            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Database migrations are pending");

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Carry the exception: a probe that can only ever say "unavailable" cannot be diagnosed.
            return HealthCheckResult.Unhealthy("Database is unavailable", exception);
        }
    }
}
