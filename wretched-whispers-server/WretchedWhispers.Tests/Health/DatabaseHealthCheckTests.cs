using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WretchedWhispers.Api.Health;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Health;

public class DatabaseHealthCheckTests
{
    [Fact]
    public async Task MigratedDatabaseIsHealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ww-health-{Guid.NewGuid():N}.db");
        try
        {
            await using var services = BuildServices($"Data Source={path}");
            await using (var scope = services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>()
                    .Database.MigrateAsync();

            var result = await CreateHealthCheck(services).CheckHealthAsync(new());
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task PendingMigrationsAreUnhealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ww-health-{Guid.NewGuid():N}.db");
        try
        {
            await using var services = BuildServices($"Data Source={path}");
            var result = await CreateHealthCheck(services).CheckHealthAsync(new());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UnavailableDatabaseIsUnhealthy()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ww-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var services = BuildServices($"Data Source={directory}");
            var result = await CreateHealthCheck(services).CheckHealthAsync(new());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<WretchedWhispersDbContext>(options => options.UseSqlite(connectionString));
        return services.BuildServiceProvider();
    }

    private static DatabaseHealthCheck CreateHealthCheck(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>());
}
