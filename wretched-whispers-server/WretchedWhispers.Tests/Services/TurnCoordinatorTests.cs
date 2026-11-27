using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class TurnCoordinatorTests
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _chatSessionId = Guid.NewGuid();

    private readonly Mock<ISessionContextLoader> _contextLoader = new();
    private readonly Mock<IAgentToolProvider> _toolProvider = new();
    private readonly Mock<IAgentExecutor> _agentExecutor = new();
    private readonly Mock<IChatHistoryRepository> _chatHistoryRepo = new();
    private readonly Mock<ITurnTraceRepository> _turnTraceRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISessionLock> _sessionLock = new();

    public TurnCoordinatorTests()
    {
        // A no-op unit of work: BeginAsync hands back a scope whose CommitAsync/DisposeAsync do nothing.
        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork
            .Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope.Object);

        // Lock is free by default; the busy-session test overrides this with null.
        _sessionLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
    }

    /// <summary>The coordinator counts quiet turns on the campaign; any campaign will do for these tests.</summary>
    private static Core.Campaigns.CampaignService AnyCampaign()
    {
        var campaigns = new Mock<Core.Campaigns.ICampaignsRepository>();
        campaigns.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Core.Campaigns.Campaign.Create(Core.Campaigns.Difficulty.Grim, "Test Campaign", "A test"));
        return new Core.Campaigns.CampaignService(
            campaigns.Object, Mock.Of<Core.Characters.ICharactersRepository>(),
            new Core.Dices.Dice(new WretchedWhispers.Infrastructure.SeededRandomService(1)));
    }

    private TurnCoordinator CreateCoordinator() =>
        new(
            _contextLoader.Object,
            AnyCampaign(),
            _toolProvider.Object,
            _agentExecutor.Object,
            _chatHistoryRepo.Object,
            _turnTraceRepo.Object,
            _unitOfWork.Object,
            _sessionLock.Object,
            TimeProvider.System,
            DetachedScopes(),
            NullLogger<TurnCoordinator>.Instance);

    /// <summary>The fresh scopes a non-committed turn's trace is written through: they resolve the same
    /// trace repository mock, so tests can see every trace regardless of outcome.</summary>
    private IServiceScopeFactory DetachedScopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _turnTraceRepo.Object);
        services.AddScoped(_ => _summarizer.Object);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private readonly Mock<IHistorySummarizer> _summarizer = new();

    private SessionContext MakeExplorationContext()
    {
        var ctx = new SessionContext { SessionId = _sessionId };
        var campaign = Core.Campaigns.Campaign.Create(
            Core.Campaigns.Difficulty.Grim, "Test Campaign", "A test");
        // Populate the Character too — the real loader always does when a character exists, and
        // StateUpdateMapper (hence the per-turn delta) reads the Character, not just the id.
        var character = TestCharacters.Create(new Core.Dices.Dice(new Infrastructure.SeededRandomService(1)));
        campaign.JoinGame(character.Id);
        campaign.Start();
        ctx.Campaign = campaign;
        ctx.Character = character;
        ctx.SetCampaignId(campaign.Id);
        ctx.SetCharacterId(character.Id);
        return ctx;
    }

    private SessionContext MakeEndedContext()
    {
        // A dead character, an ended world, and an ended campaign all derive to SessionStage.Ended
        // (see StageDerivationTests). Ending the campaign is the lightest way to reach that stage.
        var ctx = new SessionContext { SessionId = _sessionId };
        var campaign = Core.Campaigns.Campaign.Create(
            Core.Campaigns.Difficulty.Grim, "Test Campaign", "A test");
        var characterId = Guid.NewGuid();
        campaign.JoinGame(characterId);
        campaign.Start();
        campaign.End();
        ctx.Campaign = campaign;
        ctx.SetCampaignId(campaign.Id);
        ctx.SetCharacterId(characterId);
        return ctx;
    }

    private void SetupChatSession()
    {
        _chatHistoryRepo
            .Setup(r => r.GetSessionsForCampaign(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { _chatSessionId });
    }

    private void SetupToolsForExploration()
    {
        IReadOnlyList<AIFunction> tools = [];
        _toolProvider
            .Setup(f => f.GetTools(It.IsAny<SessionContext>()))
            .Returns((tools, new[] { "Character.ChallengeCharacter" }));
    }

    /// <summary>The standard arrange for a turn that reaches the agent: a chat session exists, the
    /// loader returns an active exploration context, and the stage's tools resolve.</summary>
    private void ArrangeExplorationTurn()
    {
        SetupChatSession();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeExplorationContext());
        SetupToolsForExploration();
    }

    private void SetupAgentExecutorStreaming(params GameTurnEvent[] events)
    {
        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                _chatSessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(events));
    }

    private static async IAsyncEnumerable<GameTurnEvent> ToAsyncEnumerable(
        IEnumerable<GameTurnEvent> events)
    {
        foreach (var evt in events)
        {
            yield return evt;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task BusySession_EmitsTurnError_WithoutRunningAgent()
    {
        ArrangeExplorationTurn();

        // Another turn holds the session lock.
        _sessionLock
            .Setup(l => l.TryAcquireAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var coordinator = CreateCoordinator();

        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I explore", ct: CancellationToken.None))
            events.Add(evt);

        var error = Assert.Single(events.OfType<TurnError>());
        Assert.Contains("already responding", error.Message);
        // The busy path costs nothing: the agent (and its LLM call) never runs.
        _agentExecutor.Verify(a => a.ExecuteAsync(
            It.IsAny<IReadOnlyList<AIFunction>>(),
            It.IsAny<SessionContext>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SessionState_IsLoadedOnlyAfterTheTransactionLockIsAcquired()
    {
        SetupChatSession();
        SetupToolsForExploration();
        SetupAgentExecutorStreaming(new NarrativeChunk("The door opens."));

        var transactionStarted = false;
        var lockAcquired = false;
        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .Callback(() => transactionStarted = true)
            .ReturnsAsync(scope.Object);
        _sessionLock.Setup(l => l.TryAcquireAsync(_sessionId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.True(transactionStarted);
                lockAcquired = true;
            })
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _contextLoader.Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(lockAcquired))
            .ReturnsAsync(MakeExplorationContext());

        await foreach (var _ in CreateCoordinator().ExecuteTurnAsync(
                           _sessionId, "I open the door", ct: CancellationToken.None)) { }

        Assert.True(lockAcquired);
    }

    [Fact]
    public async Task HappyPath_ProducesNarrativeStateUpdateAndTurnDone()
    {
        // Arrange
        ArrangeExplorationTurn();

        SetupAgentExecutorStreaming(
            new NarrativeChunk("The dungeon echoes..."),
            new NarrativeChunk(" You see a shadow."),
            new AgentTrace([new ToolCallTrace("Dice.Roll", "{\"expr\":\"d20\"}")]));

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I explore", ct: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Contains(events, e => e is NarrativeChunk);
        Assert.Contains(events, e => e is StateUpdate);
        Assert.IsType<TurnDone>(events[^1]);

        // The turn is captured for offline error analysis.
        _turnTraceRepo.Verify(
            r => r.Save(It.IsAny<TurnTraceEntity>(), It.IsAny<CancellationToken>()), Times.Once);

        // The AgentTrace capture event is consumed by the coordinator, never streamed to the client.
        Assert.DoesNotContain(events, e => e is AgentTrace);
    }

    [Fact]
    public async Task HappyPath_StreamsTheRunBeforeCommit_AndTheAuthoritativeEventsAfter()
    {
        // Narration and tool results reach the player as the run produces them — before the commit.
        // The delta, the committed state and done are only ever emitted once the commit succeeded.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new NarrativeChunk("You rest by the embers."),
            new ToolResult("Rest", "{\"hpHealed\":2}"),
            new AgentTrace([new ToolCallTrace("Rest", null)]));

        var commitCount = 0;
        var eventsSeenAtCommit = -1;
        var eventsCountSnapshot = 0;
        var events = new List<GameTurnEvent>();
        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Give the reader time to drain anything already written, so an early emit is observed.
                await Task.Delay(200);
                commitCount++;
                eventsSeenAtCommit = Volatile.Read(ref eventsCountSnapshot);
            });
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope.Object);

        await foreach (var evt in CreateCoordinator().ExecuteTurnAsync(_sessionId, "I rest", ct: CancellationToken.None))
        {
            events.Add(evt);
            Volatile.Write(ref eventsCountSnapshot, events.Count);
        }

        Assert.Equal(1, commitCount);
        Assert.Equal(2, eventsSeenAtCommit);
        Assert.Collection(events,
            e => Assert.Equal("You rest by the embers.", Assert.IsType<NarrativeChunk>(e).Text),
            e => Assert.Equal("Rest", Assert.IsType<ToolResult>(e).Function),
            e => Assert.IsType<TurnDelta>(e),
            e => Assert.IsType<StateUpdate>(e),
            e => Assert.IsType<TurnDone>(e));
    }

    [Fact]
    public async Task CommitThrows_EndsWithTurnError_AndNothingAuthoritative()
    {
        // The run streamed, then the commit failed. The stream must END with the error — the signal to
        // discard what this attempt showed — and must never carry a delta, a state update or done.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new NarrativeChunk("The crone takes your silver."),
            new ToolResult("BuyItem", "{\"silverSpent\":10}"),
            new AgentTrace([new ToolCallTrace("BuyItem", null)]));

        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database went away"));
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope.Object);

        var events = new List<GameTurnEvent>();
        await foreach (var evt in CreateCoordinator().ExecuteTurnAsync(_sessionId, "I buy the map", ct: CancellationToken.None))
            events.Add(evt);

        Assert.Collection(events,
            e => Assert.IsType<NarrativeChunk>(e),
            e => Assert.IsType<ToolResult>(e),
            e => Assert.Equal("An error occurred while processing your action", Assert.IsType<TurnError>(e).Message));
    }

    // ---- Trace coverage: every outcome after the stage is known leaves a comparable trace ----

    private readonly List<string> _sequence = [];

    /// <summary>Records saved traces, and the order of rollback (uow disposal) vs. trace save.</summary>
    private List<TurnTraceEntity> CaptureTraces(Exception? commitThrows = null)
    {
        var scope = new Mock<IUnitOfWorkScope>();
        var commit = scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>()));
        if (commitThrows is null) commit.Returns(Task.CompletedTask);
        else commit.ThrowsAsync(commitThrows);
        scope.Setup(s => s.DisposeAsync()).Callback(() => _sequence.Add("uow-disposed")).Returns(ValueTask.CompletedTask);
        _unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope.Object);

        var saved = new List<TurnTraceEntity>();
        _turnTraceRepo
            .Setup(r => r.Save(It.IsAny<TurnTraceEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TurnTraceEntity, CancellationToken>((t, _) => { saved.Add(t); _sequence.Add("trace-saved"); })
            .Returns(Task.CompletedTask);
        return saved;
    }

    private void OfferTools(params string[] names)
    {
        IReadOnlyList<AIFunction> tools = names.Select(n => AIFunctionFactory.Create(() => "ok", n)).ToList();
        _toolProvider
            .Setup(f => f.GetTools(It.IsAny<SessionContext>()))
            .Returns((tools, names));
    }

    private async Task<List<GameTurnEvent>> RunTurnAsync(Guid? turnId = null, int? attempt = null)
    {
        var events = new List<GameTurnEvent>();
        await foreach (var evt in CreateCoordinator().ExecuteTurnAsync(
                           _sessionId, "I explore", turnId, CancellationToken.None, attempt))
            events.Add(evt);
        return events;
    }

    [Fact]
    public async Task ExecutorThrows_WritesFailedTrace_AfterRollback_AndPersistsNoChat()
    {
        ArrangeExplorationTurn();
        OfferTools("Rest", "Roll");
        var saved = CaptureTraces();
        _agentExecutor
            .Setup(a => a.ExecuteAsync(It.IsAny<IReadOnlyList<AIFunction>>(), It.IsAny<SessionContext>(),
                _chatSessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(new InvalidOperationException("LLM exploded")));
        var turnId = Guid.NewGuid();

        await RunTurnAsync(turnId, attempt: 2);

        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Failed, trace.Outcome);
        Assert.Equal("InvalidOperationException: LLM exploded", trace.Error);
        Assert.Equal(turnId, trace.TurnId);
        Assert.Equal(2, trace.Attempt);
        Assert.Equal("[\"Rest\",\"Roll\"]", trace.ToolsOffered);
        Assert.Equal("Exploration", trace.Stage);
        Assert.NotNull(trace.DurationMs);
        Assert.Equal(new[] { "uow-disposed", "trace-saved" }, _sequence);
        _chatHistoryRepo.Verify(
            r => r.SaveMessage(It.IsAny<Guid>(), It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task EmptyResponse_WritesEmptyTrace()
    {
        ArrangeExplorationTurn();
        var saved = CaptureTraces();
        SetupAgentExecutorStreaming(new AgentTrace([], ModelId: "gpt-test", Iterations: 2));

        await RunTurnAsync();

        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Empty, trace.Outcome);
        Assert.Equal("gpt-test", trace.ModelId);
        Assert.Equal(2, trace.Iterations);
        Assert.Equal(new[] { "uow-disposed", "trace-saved" }, _sequence);
    }

    [Fact]
    public async Task CommitConflict_WritesConflictTrace_WithTheRunsToolCalls()
    {
        ArrangeExplorationTurn();
        var saved = CaptureTraces(commitThrows: new DbUpdateConcurrencyException("row version changed"));
        SetupAgentExecutorStreaming(
            new NarrativeChunk("You rest."),
            new ToolResult("Rest", "{\"hpHealed\":2}"),
            new AgentTrace([new ToolCallTrace("Rest", null)]));

        var events = await RunTurnAsync();

        Assert.IsType<TurnError>(events[^1]);
        Assert.DoesNotContain(events, e => e is TurnDelta or StateUpdate or TurnDone);
        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Conflict, trace.Outcome);
        Assert.Contains("row version changed", trace.Error);
        Assert.Contains("Rest", trace.ToolCallsJson);
        Assert.Equal("You rest.", trace.Narrative);
        Assert.Equal(new[] { "uow-disposed", "trace-saved" }, _sequence);
    }

    [Fact]
    public async Task CommittedTrace_CarriesModelPromptToolsAndUsage()
    {
        ArrangeExplorationTurn();
        OfferTools("Rest");
        var saved = CaptureTraces();
        SetupAgentExecutorStreaming(
            new NarrativeChunk("You rest."),
            new ToolResult("Rest", "{\"hpHealed\":2}"),
            new AgentTrace([new ToolCallTrace("Rest", null)],
                ModelId: "gpt-test", PromptHash: "abc123", InputTokens: 1200, OutputTokens: 85, Iterations: 2));
        var turnId = Guid.NewGuid();

        await RunTurnAsync(turnId, attempt: 1);

        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Committed, trace.Outcome);
        Assert.Null(trace.Error);
        Assert.Equal(turnId, trace.TurnId);
        Assert.Equal(1, trace.Attempt);
        Assert.Equal("gpt-test", trace.ModelId);
        Assert.Equal("abc123", trace.PromptHash);
        Assert.Equal("[\"Rest\"]", trace.ToolsOffered);
        Assert.Equal(1200, trace.InputTokens);
        Assert.Equal(85, trace.OutputTokens);
        Assert.Equal(2, trace.Iterations);
        Assert.NotNull(trace.DurationMs);
    }

    [Fact]
    public async Task CommittedTrace_CarriesTheRequestSent_AndEachModelRequestsUsage()
    {
        ArrangeExplorationTurn();
        var saved = CaptureTraces();
        SetupAgentExecutorStreaming(
            new ModelRequest("""{"instructions":"Be grim.","messages":[{"role":"user","text":"I explore"}],"tools":[]}"""),
            new NarrativeChunk("Fog, and nothing else."),
            new AgentTrace([], ModelId: "gpt-test", ModelCalls:
            [
                new ModelCall("gpt-test", InputTokens: 1200, CachedInputTokens: 1024, OutputTokens: 85,
                    ReasoningTokens: 64, DurationMs: 4200, FirstOutputMs: 900, FinishReason: "stop"),
            ]));

        var events = await RunTurnAsync();

        // Like AgentTrace, the request is for the trace only and never reaches the client.
        Assert.DoesNotContain(events, e => e is ModelRequest);
        var trace = Assert.Single(saved);
        Assert.Equal("Be grim.", JsonDocument.Parse(trace.RequestJson!).RootElement.GetProperty("instructions").GetString());
        var call = JsonDocument.Parse(trace.ModelCallsJson!).RootElement[0];
        Assert.Equal("gpt-test", call.GetProperty("modelId").GetString());
        Assert.Equal(1200, call.GetProperty("inputTokens").GetInt64());
        Assert.Equal(1024, call.GetProperty("cachedInputTokens").GetInt64());
        Assert.Equal(85, call.GetProperty("outputTokens").GetInt64());
        Assert.Equal(64, call.GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(4200, call.GetProperty("durationMs").GetInt64());
        Assert.Equal(900, call.GetProperty("firstOutputMs").GetInt64());
        Assert.Equal("stop", call.GetProperty("finishReason").GetString());
    }

    [Fact]
    public async Task FailedTurn_StillRecordsTheRequestItSent()
    {
        // The turns that go wrong are the ones worth replaying, and the request is known before the
        // model is ever called.
        ArrangeExplorationTurn();
        var saved = CaptureTraces();
        _agentExecutor
            .Setup(a => a.ExecuteAsync(It.IsAny<IReadOnlyList<AIFunction>>(), It.IsAny<SessionContext>(),
                _chatSessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(RequestThenThrow());

        await RunTurnAsync();

        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Failed, trace.Outcome);
        Assert.Equal("""{"instructions":"Be grim."}""", trace.RequestJson);
        Assert.Null(trace.ModelCallsJson);

        static async IAsyncEnumerable<GameTurnEvent> RequestThenThrow()
        {
            yield return new ModelRequest("""{"instructions":"Be grim."}""");
            await Task.CompletedTask;
            throw new InvalidOperationException("LLM exploded");
        }
    }

    // ---- The turn ledger ----

    [Fact]
    public async Task CommittedTurn_StoresItsSuccessfulToolCalls_WithThePlayersMessage()
    {
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new ToolResult("Rest", "{\"hpHealed\":2}", "{\"hours\":8}"),
            new ToolResult("Roll", "Error: bad dice", "{\"diceExpression\":\"banana\"}", Failed: true),
            new NarrativeChunk("You sleep until dawn."),
            new AgentTrace([new ToolCallTrace("Rest", "{\"hours\":8}")]));
        ChatMessage? savedPlayerMessage = null;
        _chatHistoryRepo
            .Setup(r => r.SaveMessage(_chatSessionId, It.Is<ChatMessage>(m => m.Role == ChatRole.User),
                It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<Guid, ChatMessage, CancellationToken, Guid?>((_, m, _, _) => savedPlayerMessage = m)
            .Returns(Task.CompletedTask);

        await RunTurnAsync();

        Assert.NotNull(savedPlayerMessage);
        Assert.Equal("I explore", savedPlayerMessage!.Text); // the player's words are stored as typed
        var record = TurnLedger.RecordText(savedPlayerMessage);
        Assert.Contains("Rest(hours: 8)", record);
        Assert.DoesNotContain("banana", record);
    }

    [Fact]
    public async Task TurnWithoutTools_StoresNoRecord()
    {
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(new NarrativeChunk("Fog, and nothing else."), new AgentTrace([]));
        ChatMessage? savedPlayerMessage = null;
        _chatHistoryRepo
            .Setup(r => r.SaveMessage(_chatSessionId, It.Is<ChatMessage>(m => m.Role == ChatRole.User),
                It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<Guid, ChatMessage, CancellationToken, Guid?>((_, m, _, _) => savedPlayerMessage = m)
            .Returns(Task.CompletedTask);

        await RunTurnAsync();

        Assert.Null(TurnLedger.RecordText(savedPlayerMessage!));
    }

    // ---- Summarizing runs after the turn, off the player's time ----

    [Fact]
    public async Task CommittedTurn_SummarizesAfterItsStreamEnded_OutsideTheTransactionAndLock()
    {
        ArrangeExplorationTurn();
        CaptureTraces(); // records "uow-disposed" in _sequence
        _sessionLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallbackDisposable(() => _sequence.Add("lock-released")));
        SetupAgentExecutorStreaming(new NarrativeChunk("You rest."), new AgentTrace([]));

        var summarizing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _summarizer
            .Setup(s => s.SummarizeIfDueAsync(_chatSessionId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _sequence.Add("summarize");
                summarizing.TrySetResult();
                return release.Task; // a slow summarizer model call
            });

        // The whole stream — done included — is read while the summarizer is still "running".
        var events = await RunTurnAsync();
        Assert.IsType<TurnDone>(events[^1]);

        await summarizing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(release.Task.IsCompleted);
        Assert.Equal(new[] { "lock-released", "uow-disposed", "trace-saved", "summarize" }.Order(), _sequence.Order());
        Assert.Equal("summarize", _sequence[^1]);
        release.SetResult(true);
    }

    [Fact]
    public async Task TurnThatDidNotCommit_NeverSummarizes()
    {
        ArrangeExplorationTurn();
        var saved = CaptureTraces();
        SetupAgentExecutorStreaming(new AgentTrace([])); // empty response: the turn fails

        await RunTurnAsync();

        // The failed turn's trace is written on the same detached tail the summarizer would run on.
        for (var i = 0; i < 100 && saved.Count == 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        _summarizer.Verify(
            s => s.SummarizeIfDueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SummarizerFailure_DoesNotTouchTheCompletedTurn()
    {
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(new NarrativeChunk("You rest."), new AgentTrace([]));
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _summarizer
            .Setup(s => s.SummarizeIfDueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                called.TrySetResult();
                return Task.FromException<bool>(new HttpRequestException("summarizer unavailable"));
            });

        var events = await RunTurnAsync();

        Assert.IsType<TurnDone>(events[^1]);
        Assert.DoesNotContain(events, e => e is TurnError);
        await called.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private sealed class CallbackDisposable(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }

    // ---- Retryable failures and cancellation ----

    private void SetupExecutor(Func<IReadOnlyList<AIFunction>, CancellationToken, IAsyncEnumerable<GameTurnEvent>> run) =>
        _agentExecutor
            .Setup(a => a.ExecuteAsync(It.IsAny<IReadOnlyList<AIFunction>>(), It.IsAny<SessionContext>(),
                _chatSessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<AIFunction> tools, SessionContext _, Guid _, string _, CancellationToken ct) =>
                run(tools, ct));

    private static async IAsyncEnumerable<GameTurnEvent> InvokeFirstToolThenThrow(
        IReadOnlyList<AIFunction> tools, Exception ex)
    {
        await tools[0].InvokeAsync(new AIFunctionArguments());
        if (tools.Count >= 0) throw ex;
        yield break;
    }

    [Fact]
    public async Task TransientFailureBeforeAnyTool_IsMarkedRetryable()
    {
        ArrangeExplorationTurn();
        OfferTools("Rest");
        SetupExecutor((_, _) => ThrowingAsyncEnumerable(new HttpRequestException("503 from upstream")));

        var error = Assert.IsType<TurnError>(Assert.Single(await RunTurnAsync()));

        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task TransientFailureAfterAToolStarted_IsNotRetryable()
    {
        ArrangeExplorationTurn();
        OfferTools("Rest");
        SetupExecutor((tools, _) => InvokeFirstToolThenThrow(tools, new HttpRequestException("503 from upstream")));

        var error = Assert.IsType<TurnError>(Assert.Single(await RunTurnAsync()));

        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task NonTransientFailure_IsNotRetryable()
    {
        ArrangeExplorationTurn();
        SetupExecutor((_, _) => ThrowingAsyncEnumerable(new InvalidOperationException("a bug")));

        var error = Assert.IsType<TurnError>(Assert.Single(await RunTurnAsync()));

        Assert.False(error.Retryable);
    }

    [Fact]
    public void RetryableFlag_IsNeverSerializedToTheClient()
    {
        var json = JsonSerializer.Serialize<GameTurnEvent>(new TurnError("boom", Retryable: true), JsonSerializerOptions.Web);

        Assert.DoesNotContain("retryable", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledRun_WritesCancelledTrace_AfterRollback_AndEmitsNothing()
    {
        ArrangeExplorationTurn();
        var saved = CaptureTraces();
        using var cts = new CancellationTokenSource();
        SetupExecutor((_, ct) => CancelThenWait(cts, ct));

        var events = new List<GameTurnEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in CreateCoordinator().ExecuteTurnAsync(_sessionId, "I explore", ct: cts.Token))
                events.Add(evt);
        });

        Assert.Empty(events);
        // The reader is cancelled at once; the producer finishes its rollback and trace just after.
        for (var i = 0; i < 100 && saved.Count == 0; i++) await Task.Delay(20);
        var trace = Assert.Single(saved);
        Assert.Equal(TurnTraceOutcome.Cancelled, trace.Outcome);
        Assert.Equal(new[] { "uow-disposed", "trace-saved" }, _sequence);
    }

    private static async IAsyncEnumerable<GameTurnEvent> CancelThenWait(
        CancellationTokenSource cts, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await cts.CancelAsync();
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    [Fact]
    public async Task Trace_PersistsTurnDelta_SoEvalsCanSeeWhatActuallyChanged()
    {
        // The turn trace must carry the authoritative delta, not just the narrative — that is what lets
        // offline eval analysis tell a real outcome from a fabricated one. Here pre-state == post-state
        // (same context returned for both loads), so the persisted delta records a no-op: the ground truth
        // that would contradict any narrative claiming silver spent or an item gained.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new NarrativeChunk("You haggle, but the crone waves you off."),
            new AgentTrace([]));

        TurnTraceEntity? saved = null;
        _turnTraceRepo
            .Setup(r => r.Save(It.IsAny<TurnTraceEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TurnTraceEntity, CancellationToken>((t, _) => saved = t)
            .Returns(Task.CompletedTask);

        var coordinator = CreateCoordinator();
        await foreach (var _ in coordinator.ExecuteTurnAsync(_sessionId, "I buy the map", ct: CancellationToken.None)) { }

        Assert.NotNull(saved);
        Assert.NotNull(saved!.TurnDeltaJson);
        var delta = JsonSerializer.Deserialize<TurnDelta>(
            saved.TurnDeltaJson!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(delta);
        Assert.True(delta!.IsNoOp);
    }

    [Fact]
    public async Task EmptyAgentResponse_ProducesTurnError_AndPersistsNothing()
    {
        // The model returned neither prose nor tool calls (empty response / content filter). The turn
        // must FAIL — not complete silently — and nothing may be persisted, or an empty assistant
        // message pollutes every later prompt.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(new AgentTrace([]));

        var coordinator = CreateCoordinator();

        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I explore", ct: CancellationToken.None))
            events.Add(evt);

        var error = events.OfType<TurnError>().Single();
        Assert.Contains("narrator fell silent", error.Message);
        Assert.DoesNotContain(events, e => e is TurnDone);
        _chatHistoryRepo.Verify(
            r => r.SaveMessage(It.IsAny<Guid>(), It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task ToolOnlyTurn_Commits_ButNeverPersistsEmptyAssistantMessage()
    {
        // Tools ran but the model emitted no prose: the state change is real (commit, TurnDone), but an
        // EMPTY assistant message must not enter the history the model sees on later turns.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new ToolResult("Rest", "{\"hpHealed\":2}"),
            new AgentTrace([new ToolCallTrace("Rest", null)]));

        var coordinator = CreateCoordinator();

        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I rest", ct: CancellationToken.None))
            events.Add(evt);

        Assert.IsType<TurnDone>(events[^1]);
        _chatHistoryRepo.Verify(
            r => r.SaveMessage(_chatSessionId,
                It.Is<ChatMessage>(m => m.Role == ChatRole.User), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Once);
        _chatHistoryRepo.Verify(
            r => r.SaveMessage(_chatSessionId,
                It.Is<ChatMessage>(m => m.Role == ChatRole.Assistant), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    public static TheoryData<Exception, string> AgentFailures => new()
    {
        // Any unexpected failure becomes a generic error; a concurrency conflict (another turn
        // committed first) gets the dedicated retry message.
        { new InvalidOperationException("LLM exploded"), "An error occurred while processing your action" },
        { new DbUpdateConcurrencyException("conflict"), "This session was updated by another action. Please retry." }
    };

    [Theory]
    [MemberData(nameof(AgentFailures))]
    public async Task AgentExecutorThrows_ProducesTurnError_AndNoTurnDone(Exception thrown, string expectedMessage)
    {
        // Arrange
        ArrangeExplorationTurn();

        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                _chatSessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(thrown));

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "attack", ct: CancellationToken.None))
            events.Add(evt);

        // Assert
        var error = events.OfType<TurnError>().First();
        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(events, e => e is TurnDone);
    }

    [Fact]
    public async Task NoCampaign_ProducesTurnErrorSessionNotFound()
    {
        // Arrange
        SetupChatSession();

        var emptyContext = new SessionContext { SessionId = _sessionId };
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyContext);

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "hello", ct: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Single(events);
        var error = Assert.IsType<TurnError>(events[0]);
        Assert.Equal("Session not found", error.Message);
    }

    [Fact]
    public async Task NoChatSession_ProducesTurnErrorNoChatSession()
    {
        // Arrange — GetSessionsForCampaign returns empty list
        _chatHistoryRepo
            .Setup(r => r.GetSessionsForCampaign(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "hello", ct: CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Single(events);
        var error = Assert.IsType<TurnError>(events[0]);
        Assert.Equal("No chat session found for this campaign", error.Message);
    }

    [Fact]
    public async Task EndedSession_RefusesTurn_WithoutRunningNarrator()
    {
        // Arrange — the game is over (dead character / ended world). The player still sends an action.
        SetupChatSession();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEndedContext());

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "continue", ct: CancellationToken.None))
            events.Add(evt);

        // Assert — the client is re-synced to the ended state, then the turn is refused. No narration
        // (the model could fabricate a revival), no TurnDone.
        Assert.Contains(events, e => e is StateUpdate);
        var error = events.OfType<TurnError>().Single();
        Assert.Equal("This story has ended. Begin a new character to continue.", error.Message);
        Assert.DoesNotContain(events, e => e is NarrativeChunk);
        Assert.DoesNotContain(events, e => e is TurnDone);

        // A refused turn leaves a trace that says so, with nothing offered or called.
        _turnTraceRepo.Verify(
            r => r.Save(It.Is<TurnTraceEntity>(t =>
                    t.Outcome == TurnTraceOutcome.Refused
                    && t.Error == "This story has ended. Begin a new character to continue."
                    && t.ToolsOffered == "[]"
                    && t.ToolCallsJson == "[]"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Domain authority: the narrator is NEVER invoked on a finished game.
        _agentExecutor.Verify(
            a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async IAsyncEnumerable<GameTurnEvent> ThrowingAsyncEnumerable(Exception ex)
    {
        await Task.CompletedTask;
        throw ex;
#pragma warning disable CS0162 // Unreachable code — needed so compiler treats this as IAsyncEnumerable
        yield break;
#pragma warning restore CS0162
    }
}
