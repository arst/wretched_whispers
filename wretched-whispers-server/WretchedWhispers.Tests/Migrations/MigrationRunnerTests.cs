using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Migrations;
using Xunit;

namespace WretchedWhispers.Tests.Migrations;

public class MigrationRunnerTests
{
    [Fact]
    public async Task FreshDatabaseMigratesAndSecondRunIsANoOp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ww-migrations-{Guid.NewGuid():N}.db");
        var connection = $"Data Source={path}";
        try
        {
            var firstLog = new StringWriter();
            Assert.Equal(0, await MigrationRunner.RunAsync("sqlite", connection, firstLog));
            Assert.Contains("Applied:", firstLog.ToString());

            await using var db = new WretchedWhispersDbContext(
                new DbContextOptionsBuilder<WretchedWhispersDbContext>().UseSqlite(connection).Options);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var secondLog = new StringWriter();
            Assert.Equal(0, await MigrationRunner.RunAsync("sqlite", connection, secondLog));
            Assert.Contains("Pending migrations: ", secondLog.ToString());
            Assert.DoesNotContain("Applied:", secondLog.ToString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, "Data Source=test.db")]
    [InlineData("other", "Data Source=test.db")]
    [InlineData("sqlite", null)]
    public async Task InvalidConfigurationReturnsTwo(string? provider, string? connection)
    {
        Assert.Equal(2, await MigrationRunner.RunAsync(provider, connection, TextWriter.Null));
    }

    [Fact]
    public async Task MigrationFailureReturnsOne()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ww-migrations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Equal(1, await MigrationRunner.RunAsync(
                "sqlite", $"Data Source={directory}", TextWriter.Null));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
