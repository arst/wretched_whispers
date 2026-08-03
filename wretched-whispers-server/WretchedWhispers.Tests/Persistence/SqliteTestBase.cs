using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Serialization;

namespace WretchedWhispers.Tests.Persistence;

public class SqliteTestBase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new WretchedWhispersDbContext(options);
        Db.Database.EnsureCreated();

        JsonOptions = AggregateJsonOptions.Create();
    }

    public WretchedWhispersDbContext Db { get; }
    public JsonSerializerOptions JsonOptions { get; }
    public IUserContext UserContext { get; } = new StubUserContext();

    /// <summary>
    /// A second <see cref="WretchedWhispersDbContext"/> over the same in-memory database (shared
    /// connection). Used to simulate two concurrent request scopes — each with its own change
    /// tracker — racing on the same row.
    /// </summary>
    public WretchedWhispersDbContext CreateSeparateContext()
    {
        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WretchedWhispersDbContext(options);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    private sealed class StubUserContext : IUserContext
    {
        public string UserId { get; private set; } = "test-user";
        public void SetUserId(string userId) => UserId = userId;
    }
}
