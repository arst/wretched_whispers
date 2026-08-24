using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Worker-level wiring for the terminal-event invariant: every turn ends with exactly ONE
/// terminal event, written by queue finalization — never appended from the stream by the worker.
/// Uses a file-backed SQLite database so the worker's scopes and the test's assertions run on
/// separate connections, like separate request scopes in production.
/// </summary>
public sealed class TurnWorkerTests : IDisposable
{
    private const string UserId = "test-user";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ww-turnworker-{Guid.NewGuid():N}.db");
    private readonly Mock<IChatHistoryRepository> _chatHistory = new();
    private readonly Mock<IAgentExecutor> _agentExecutor = new();

    public TurnWorkerTests()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();

        // No chat session for the campaign: the coordinator streams a TurnError for such turns.
        _chatHistory
            .Setup(r => r.GetSessionsForCampaign(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _chatHistory
            .Setup(r => r.HasMessagesForTurn(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_dbPath + suffix);
    }

    [Fact]
    public async Task CoordinatorError_EndsWithFailedStatusAndExactlyOneErrorTerminalEvent()
    {
        var turnId = await EnqueueAsync();

        await RunWorkerUntilTerminalAsync(turnId);

        await using var db = CreateContext();
        var turn = await db.TurnRequests.SingleAsync(x => x.Id == turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        // The worker must not have appended the streamed TurnError itself — finalization writes
        // the single terminal event with the captured message.
        var terminal = Assert.Single(await db.TurnEvents.Where(x => x.TurnId == turnId).ToListAsync());
        Assert.Equal("error", terminal.EventType);
    }

    [Fact]
    public async Task CommittedButUnfinalizedTurn_IsReplayedToDoneWithoutRunningTheAgent()
    {
        // The crash-replay path: a previous owner committed the domain transaction (a chat message
        // carries the turn id) but died before finalizing.
        _chatHistory
            .Setup(r => r.HasMessagesForTurn(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var turnId = await EnqueueAsync();

        await RunWorkerUntilTerminalAsync(turnId);

        await using var db = CreateContext();
        var turn = await db.TurnRequests.SingleAsync(x => x.Id == turnId);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        var terminal = Assert.Single(await db.TurnEvents.Where(x => x.TurnId == turnId).ToListAsync());
        Assert.Equal("done", terminal.EventType);
        _agentExecutor.Verify(a => a.ExecuteAsync(
            It.IsAny<IReadOnlyList<AIFunction>>(),
            It.IsAny<SessionContext>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private WretchedWhispersDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite($"DataSource={_dbPath}")
            .Options);

    private async Task<Guid> EnqueueAsync()
    {
        await using var db = CreateContext();
        var queue = new TurnQueue(db, TimeProvider.System);
        var queued = await queue.EnqueueAsync(
            Guid.NewGuid(), UserId, Guid.NewGuid(), "I open the door.", CancellationToken.None);
        return queued.Turn!.Id;
    }

    private async Task RunWorkerUntilTerminalAsync(Guid turnId)
    {
        await using var provider = BuildWorkerProvider();
        var worker = new TurnWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TurnEventStore>(),
            NullLogger<TurnWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                await using var db = CreateContext();
                var status = await db.TurnRequests.Where(x => x.Id == turnId)
                    .Select(x => x.Status).SingleAsync();
                if (status is TurnStatus.Completed or TurnStatus.Failed) return;
                Assert.True(DateTime.UtcNow < deadline, "worker did not finalize the turn in time");
                await Task.Delay(50);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private ServiceProvider BuildWorkerProvider()
    {
        var uowScope = new Mock<IUnitOfWorkScope>();
        uowScope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uowScope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowScope.Object);
        var sessionLock = new Mock<ISessionLock>();
        sessionLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => CreateContext());
        services.AddScoped<TurnQueue>();
        services.AddSingleton<TurnEventStore>();
        services.AddSingleton<IUserContext>(new WorkerUserContext());
        services.AddSingleton(_chatHistory.Object);
        services.AddScoped(_ => new TurnCoordinator(
            Mock.Of<ISessionContextLoader>(),
            Mock.Of<IAgentToolProvider>(),
            _agentExecutor.Object,
            _chatHistory.Object,
            Mock.Of<ITurnTraceRepository>(),
            unitOfWork.Object,
            sessionLock.Object,
            TimeProvider.System,
            NullLogger<TurnCoordinator>.Instance));
        return services.BuildServiceProvider();
    }

    private sealed class WorkerUserContext : IUserContext
    {
        public string UserId { get; private set; } = "";
        public void SetUserId(string userId) => UserId = userId;
    }
}
