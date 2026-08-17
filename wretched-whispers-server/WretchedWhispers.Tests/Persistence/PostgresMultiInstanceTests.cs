using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public sealed class PostgresMultiInstanceTests
{
    [PostgresFact]
    public async Task TwoApplicationInstances_ShareTheSessionLockAndFenceFinalization()
    {
        var connectionString = Environment.GetEnvironmentVariable("WW_POSTGRES_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            "CI must provide WW_POSTGRES_TEST_CONNECTION; local runs skip without it.");

        await using (var setup = CreateContext(connectionString!))
            await setup.Database.MigrateAsync();

        await using var instanceA = CreateInstance(connectionString!);
        await using var instanceB = CreateInstance(connectionString!);
        await using var scopeA = instanceA.CreateAsyncScope();
        await using var scopeB = instanceB.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        var lockA = scopeA.ServiceProvider.GetRequiredService<ISessionLock>();
        var lockB = scopeB.ServiceProvider.GetRequiredService<ISessionLock>();
        var sessionId = Guid.NewGuid();

        await using (var transactionA = await dbA.Database.BeginTransactionAsync())
        await using (var transactionB = await dbB.Database.BeginTransactionAsync())
        {
            await using var leaseA = await lockA.TryAcquireAsync(sessionId, CancellationToken.None);
            Assert.NotNull(leaseA);
            Assert.Null(await lockB.TryAcquireAsync(sessionId, CancellationToken.None));

            await transactionA.RollbackAsync();
            await using var leaseB = await lockB.TryAcquireAsync(sessionId, CancellationToken.None);
            Assert.NotNull(leaseB);
            await transactionB.RollbackAsync();
        }

        var queueA = scopeA.ServiceProvider.GetRequiredService<TurnQueue>();
        var queueB = scopeB.ServiceProvider.GetRequiredService<TurnQueue>();
        var queued = await queueA.EnqueueAsync(
            sessionId, "user", Guid.NewGuid(), "I open the door.", CancellationToken.None);
        var claimed = await queueA.ClaimAsync("instance-a", TimeSpan.FromMinutes(5), 3, CancellationToken.None);
        Assert.NotNull(claimed);

        Assert.False(await queueB.FinalizeAsync(claimed!.Id, "instance-b", null, CancellationToken.None));
        Assert.True(await queueA.FinalizeAsync(claimed.Id, "instance-a", null, CancellationToken.None));

        var completed = await queueB.GetOwnedAsync(claimed.Id, "user", CancellationToken.None);
        Assert.Equal(TurnStatus.Completed, completed!.Status);
        var terminal = await dbB.TurnEvents.SingleAsync(x => x.TurnId == claimed.Id);
        Assert.Equal("done", terminal.EventType);
    }

    private static ServiceProvider CreateInstance(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<WretchedWhispersDbContext, PostgresWwDbContext>(
            options => options.UseNpgsql(connectionString));
        services.AddScoped<ISessionLock, PostgresSessionLock>();
        services.AddScoped<TurnQueue>();
        return services.BuildServiceProvider();
    }

    private static PostgresWwDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PostgresWwDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WW_POSTGRES_TEST_CONNECTION")) &&
            !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "WW_POSTGRES_TEST_CONNECTION is not configured.";
    }
}
