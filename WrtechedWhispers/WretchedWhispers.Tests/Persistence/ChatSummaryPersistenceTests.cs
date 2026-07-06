using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Repositories;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public sealed class ChatSummaryPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WretchedWhispersDbContext _db;
    private readonly SqliteChatHistoryRepository _repo;

    public ChatSummaryPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection).Options;
        _db = new WretchedWhispersDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new SqliteChatHistoryRepository(_db);
    }

    [Fact]
    public async Task GetSummary_NoSummarySaved_ReturnsNull()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        Assert.Null(await _repo.GetSummary(sessionId));
    }

    [Fact]
    public async Task SaveSummary_ThenGet_RoundTrips()
    {
        var sessionId = await _repo.CreateSession(Guid.NewGuid());
        await _repo.SaveSummary(sessionId, new ChatSummary("the tale so far", 42));

        var summary = await _repo.GetSummary(sessionId);

        Assert.NotNull(summary);
        Assert.Equal("the tale so far", summary.Text);
        Assert.Equal(42, summary.CoveredCount);
    }

    [Fact]
    public async Task SaveSummary_UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.SaveSummary(Guid.NewGuid(), new ChatSummary("x", 1)));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
