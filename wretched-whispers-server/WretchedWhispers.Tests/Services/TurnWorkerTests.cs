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
    public async Task AttemptThatDiesBeforeCommit_ThenReclaimedRetry_VoidsItsNarrationWithAReset()
    {
        // Attempt 1 streams its narration, then its worker dies while committing; the lease expires and
        // attempt 2 (another worker) completes the turn. The event log is append-only, so attempt 1's
        // narration is still in it — followed by a reset. Reading the log the way a client does (a
        // reset discards everything before it) must show exactly ONE narration.
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        var attempts = 0;
        SetupExecutor(_ => Stream(
            new NarrativeChunk($"The door groans open (attempt {Interlocked.Increment(ref attempts)})."),
            new AgentTrace([])));

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
        await WaitForEventsAsync(turnId, TimeSpan.FromSeconds(5));
        await first.StopAsync(CancellationToken.None);

        var (afterCrash, crashEvents) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Running, afterCrash.Status); // never finalized: the worker "died"
        Assert.Equal("narrative", Assert.Single(crashEvents).EventType); // streamed before the commit

        clock.Advance(TimeSpan.FromMinutes(10)); // past the 5-minute lease: the turn is reclaimable
        await RunWorkerUntilTerminalAsync(turnId, provider);

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal(2, turn.AttemptCount);
        Assert.Equal(
            new[] { "narrative", "reset", "narrative", "turn_delta", "state_update", "done" },
            events.Select(e => e.EventType));
        var shown = Assert.Single(AsAClientSeesThem(events), e => e.EventType == "narrative");
        Assert.Contains("attempt 2", shown.Payload);
    }

    /// <summary>What a client is left with after reading the log: a reset discards all before it.</summary>
    private static List<TurnEventEntity> AsAClientSeesThem(List<TurnEventEntity> events) =>
        events.Skip(events.FindLastIndex(e => e.EventType == TurnWorker.ResetEvent) + 1).ToList();

    // ---- Deadline, cancel, transient retry ----

    private static readonly TurnWorkerOptions FastOptions = new()
    {
        CancelPollInterval = TimeSpan.FromMilliseconds(50),
        RetryBackoff = TimeSpan.FromMilliseconds(10),
    };

    private void ArrangeRunnableTurn() =>
        _chatHistory
            .Setup(r => r.GetSessionsForCampaign(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });

    private void SetupExecutor(Func<CancellationToken, IAsyncEnumerable<GameTurnEvent>> run) =>
        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(), It.IsAny<SessionContext>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<AIFunction> _, SessionContext _, Guid _, string _, CancellationToken ct) => run(ct));

    private static async IAsyncEnumerable<GameTurnEvent> Hang(
        TaskCompletionSource started, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    private static async IAsyncEnumerable<GameTurnEvent> Throw(Exception ex)
    {
        await Task.CompletedTask;
        if (ex is not null) throw ex;
        yield break;
    }

    private async Task<(TurnRequestEntity Turn, List<TurnEventEntity> Events)> LoadAsync(Guid turnId)
    {
        await using var db = CreateContext();
        return (await db.TurnRequests.SingleAsync(x => x.Id == turnId),
            await db.TurnEvents.Where(x => x.TurnId == turnId).OrderBy(x => x.Sequence).ToListAsync());
    }

    [Fact]
    public async Task TurnPastItsDeadline_FailsWithTheDeadlineMessage()
    {
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        SetupExecutor(ct => Hang(new TaskCompletionSource(), ct));

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions with { Deadline = TimeSpan.FromMilliseconds(300) });

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(TurnWorker.DeadlineMessage, turn.TerminalError);
        Assert.Equal(1, turn.AttemptCount);
        Assert.Equal("error", Assert.Single(events).EventType);
    }

    [Fact]
    public async Task TurnsOfDifferentGames_RunAtTheSameTime()
    {
        // Two players in two games share nothing, so neither waits for the other's narrator.
        await EnqueueAsync();
        await EnqueueAsync();
        ArrangeRunnableTurn();
        var running = 0;
        var bothRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupExecutor(ct =>
        {
            if (Interlocked.Increment(ref running) == 2) bothRunning.TrySetResult();
            return Hang(new TaskCompletionSource(), ct);
        });

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        var worker = CreateWorker(provider, FastOptions with { ConcurrentTurns = 2 });
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await bothRunning.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CancelRequestedWhileRunning_StopsTheRun_AndFailsAsCancelled()
    {
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupExecutor(ct => Hang(running, ct));

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        var worker = CreateWorker(provider, FastOptions);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await running.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using (var db = CreateContext())
                Assert.Equal(TurnCancelResult.Requested,
                    await new TurnQueue(db, TimeProvider.System).RequestCancelAsync(turnId, UserId, CancellationToken.None));
            await WaitUntilTerminalAsync(turnId);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(TurnQueue.CancelledMessage, turn.TerminalError);
        Assert.Equal("error", Assert.Single(events).EventType);
    }

    [Fact]
    public async Task RunStoppedAfterItsCommitLanded_CompletesTheTurn()
    {
        // The deadline (or a cancel) fired, but the turn's messages are in the chronicle: the commit won
        // the race. The turn happened, so it must complete, never be reported as failed.
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        _chatHistory
            .SetupSequence(r => r.HasMessagesForTurn(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)  // the pre-run duplicate check
            .ReturnsAsync(true);  // the check after the run was stopped
        SetupExecutor(ct => Hang(new TaskCompletionSource(), ct));

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions with { Deadline = TimeSpan.FromMilliseconds(300) });

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Null(turn.TerminalError);
        Assert.Equal("done", Assert.Single(events).EventType);
    }

    [Fact]
    public async Task TransientFailure_IsRequeued_AndTheSecondAttemptCompletes()
    {
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        var calls = 0;
        SetupExecutor(_ => Interlocked.Increment(ref calls) == 1
            ? Throw(new HttpRequestException("503 from upstream"))
            : Stream(new NarrativeChunk("The door groans open."), new AgentTrace([])));

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions);

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal(2, turn.AttemptCount);
        Assert.Equal(
            new[] { "reset", "narrative", "turn_delta", "state_update", "done" }, events.Select(e => e.EventType));
    }

    [Fact]
    public async Task FailedTurn_KeepsItsStreamedNarrationInTheLog_EndingWithTheError()
    {
        // The narration streamed, then the run failed. The log ends with the error, which is the
        // client's signal to discard the attempt's text.
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        SetupExecutor(_ => StreamThenThrow(
            new NarrativeChunk("You reach for the latch—"), new InvalidOperationException("a bug")));

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions);

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(new[] { "narrative", "error" }, events.Select(e => e.EventType));
    }

    private static async IAsyncEnumerable<GameTurnEvent> StreamThenThrow(GameTurnEvent first, Exception ex)
    {
        yield return first;
        await Task.CompletedTask;
        throw ex;
    }

    [Fact]
    public async Task TransientFailure_OnTheLastAllowedAttempt_FailsWithItsOwnError()
    {
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        var calls = 0;
        SetupExecutor(_ =>
        {
            Interlocked.Increment(ref calls);
            return Throw(new HttpRequestException("503 from upstream"));
        });

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions with { MaxAttempts = 2 });

        var (turn, events) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal("An error occurred while processing your action", turn.TerminalError);
        Assert.Equal(2, turn.AttemptCount);
        Assert.Equal(2, calls);
        Assert.Equal(new[] { "reset", "error" }, events.Select(e => e.EventType));
    }

    [Fact]
    public async Task NonTransientFailure_IsNotRetried()
    {
        var turnId = await EnqueueAsync();
        ArrangeRunnableTurn();
        var calls = 0;
        SetupExecutor(_ =>
        {
            Interlocked.Increment(ref calls);
            return Throw(new InvalidOperationException("a bug"));
        });

        await using var provider = BuildWorkerProvider(contextLoader: ExplorationContextLoader());
        await RunWorkerUntilTerminalAsync(turnId, provider, FastOptions);

        var (turn, _) = await LoadAsync(turnId);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(1, turn.AttemptCount);
        Assert.Equal(1, calls);
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

    private static TurnWorker CreateWorker(ServiceProvider provider, TurnWorkerOptions? options = null) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TurnEventStore>(),
            NullLogger<TurnWorker>.Instance,
            options);

    private async Task WaitUntilTerminalAsync(Guid turnId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using var db = CreateContext();
            var status = await db.TurnRequests.Where(x => x.Id == turnId).Select(x => x.Status).SingleAsync();
            if (status is TurnStatus.Completed or TurnStatus.Failed) return;
            Assert.True(DateTime.UtcNow < deadline, "worker did not finalize the turn in time");
            await Task.Delay(50);
        }
    }

    private async Task RunWorkerUntilTerminalAsync(
        Guid turnId, ServiceProvider provider, TurnWorkerOptions? options = null)
    {
        var worker = CreateWorker(provider, options);
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
            .Setup(p => p.GetTools(It.IsAny<SessionContext>()))
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
        // The coordinator counts quiet turns on the campaign; any campaign will do for these tests.
        var campaigns = new Mock<Core.Campaigns.ICampaignsRepository>();
        campaigns.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Core.Campaigns.Campaign.Create(Core.Campaigns.Difficulty.Grim, "Test Campaign", "A test"));
        var campaignService = new Core.Campaigns.CampaignService(
            campaigns.Object, Mock.Of<Core.Characters.ICharactersRepository>(),
            new Core.Dices.Dice(new SeededRandomService(1)));
        services.AddScoped(sp => new TurnCoordinator(
            contextLoader ?? Mock.Of<ISessionContextLoader>(),
            campaignService,
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
