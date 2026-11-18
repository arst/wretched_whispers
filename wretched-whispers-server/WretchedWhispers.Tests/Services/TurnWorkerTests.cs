using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Engine.Models;
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

    [Fact]
    public async Task AttemptThatDiesBeforeCommit_ThenReclaimedRetry_LeavesExactlyOneNarrative()
    {
        // Attempt 1 runs the agent but its worker dies while committing; the lease then expires and
        // attempt 2 (another worker) completes the turn. Because outcome events are emitted only after
        // commit, attempt 1 must leave nothing in the event store, so the replay shows ONE narrative.
        var turnId = await EnqueueAsync();
        _chatHistory
            .Setup(r => r.GetSessionsForCampaign(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(), It.IsAny<SessionContext>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Stream(
                new NarrativeChunk("The door groans open."),
                new AgentTrace([], SuppressedNarrative: null)));

        var commitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commits = 0;
        var uowScope = new Mock<IUnitOfWorkScope>();
        uowScope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref commits) > 1) return;
                // Attempt 1: hang in commit until the worker is torn down — a crash mid-commit.
                commitReached.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            });
        uowScope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowScope.Object);

        var clock = new ShiftableTimeProvider();
        await using var provider = BuildWorkerProvider(unitOfWork.Object, ExplorationContextLoader(), clock);

        var first = CreateWorker(provider);
        await first.StartAsync(CancellationToken.None);
        await commitReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // Give the worker time to persist anything the coordinator already emitted (as it would have
        // before a real crash), so an event leaked ahead of the commit is observed rather than cancelled.
        await WaitForEventsAsync(turnId, TimeSpan.FromSeconds(1));
        await first.StopAsync(CancellationToken.None);

        await using (var db = CreateContext())
        {
            var turn = await db.TurnRequests.SingleAsync(x => x.Id == turnId);
            Assert.Equal(TurnStatus.Running, turn.Status); // never finalized: the worker "died"
            Assert.Empty(await db.TurnEvents.Where(x => x.TurnId == turnId).ToListAsync());
        }

        clock.Advance(TimeSpan.FromMinutes(10)); // past the 5-minute lease: the turn is reclaimable
        await RunWorkerUntilTerminalAsync(turnId, provider);

        await using (var db = CreateContext())
        {
            var turn = await db.TurnRequests.SingleAsync(x => x.Id == turnId);
            Assert.Equal(TurnStatus.Completed, turn.Status);
            Assert.Equal(2, turn.AttemptCount);
            var events = await db.TurnEvents.Where(x => x.TurnId == turnId).OrderBy(x => x.Sequence).ToListAsync();
            Assert.Single(events, e => e.EventType == "narrative");
            Assert.Equal("done", events[^1].EventType);
        }
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
        await RunWorkerUntilTerminalAsync(turnId, provider);
    }

    private async Task WaitForEventsAsync(Guid turnId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = CreateContext();
            if (await db.TurnEvents.AnyAsync(x => x.TurnId == turnId)) return;
            await Task.Delay(50);
        }
    }

    private static TurnWorker CreateWorker(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TurnEventStore>(),
            NullLogger<TurnWorker>.Instance);

    private async Task RunWorkerUntilTerminalAsync(Guid turnId, ServiceProvider provider)
    {
        var worker = CreateWorker(provider);
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

    private ServiceProvider BuildWorkerProvider(
        IUnitOfWork? unitOfWork = null, ISessionContextLoader? contextLoader = null, TimeProvider? clock = null)
    {
        if (unitOfWork is null)
        {
            var uowScope = new Mock<IUnitOfWorkScope>();
            uowScope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            uowScope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(uowScope.Object);
            unitOfWork = uow.Object;
        }
        var toolProvider = new Mock<IAgentToolProvider>();
        toolProvider
            .Setup(p => p.GetToolsForStage(It.IsAny<SessionContext>(), It.IsAny<SessionStage>()))
            .Returns(((IReadOnlyList<AIFunction>)[], Array.Empty<string>()));
        var sessionLock = new Mock<ISessionLock>();
        sessionLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        var services = new ServiceCollection();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddScoped(_ => CreateContext());
        services.AddScoped<TurnQueue>();
        services.AddSingleton<TurnEventStore>();
        services.AddSingleton<IUserContext>(new WorkerUserContext());
        services.AddSingleton(_chatHistory.Object);
        services.AddSingleton(Mock.Of<ITurnTraceRepository>());
        services.AddScoped(sp => new TurnCoordinator(
            contextLoader ?? Mock.Of<ISessionContextLoader>(),
            toolProvider.Object,
            _agentExecutor.Object,
            _chatHistory.Object,
            sp.GetRequiredService<ITurnTraceRepository>(),
            unitOfWork,
            sessionLock.Object,
            TimeProvider.System,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TurnCoordinator>.Instance));
        return services.BuildServiceProvider();
    }

    /// <summary>A context loader that always returns an active exploration session with a character.</summary>
    private static ISessionContextLoader ExplorationContextLoader()
    {
        var loader = new Mock<ISessionContextLoader>();
        loader.Setup(l => l.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid sessionId, CancellationToken _) =>
            {
                var ctx = new SessionContext { SessionId = sessionId };
                var campaign = Core.Campaigns.Campaign.Create(Core.Campaigns.Difficulty.Grim, "Test Campaign", "A test");
                var character = TestCharacters.Create(new Core.Dices.Dice(new SeededRandomService(1)));
                campaign.JoinGame(character.Id);
                campaign.Start();
                ctx.Campaign = campaign;
                ctx.Character = character;
                ctx.SetCampaignId(campaign.Id);
                ctx.SetCharacterId(character.Id);
                return ctx;
            });
        return loader.Object;
    }

    private static async IAsyncEnumerable<GameTurnEvent> Stream(params GameTurnEvent[] events)
    {
        foreach (var evt in events)
            yield return evt;
        await Task.CompletedTask;
    }

    /// <summary>System time plus an offset the test can advance, to expire a lease without waiting.</summary>
    private sealed class ShiftableTimeProvider : TimeProvider
    {
        private long _offsetTicks;
        public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
        public override DateTimeOffset GetUtcNow() =>
            System.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));
    }

    private sealed class WorkerUserContext : IUserContext
    {
        public string UserId { get; private set; } = "";
        public void SetUserId(string userId) => UserId = userId;
    }
}
