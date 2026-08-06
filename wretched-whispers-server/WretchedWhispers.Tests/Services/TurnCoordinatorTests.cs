using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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

    private TurnCoordinator CreateCoordinator() =>
        new(
            _contextLoader.Object,
            _toolProvider.Object,
            _agentExecutor.Object,
            _chatHistoryRepo.Object,
            _turnTraceRepo.Object,
            _unitOfWork.Object,
            _sessionLock.Object,
            TimeProvider.System,
            NullLogger<TurnCoordinator>.Instance);

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
            .Setup(f => f.GetToolsForStage(It.IsAny<SessionContext>(), SessionStage.Exploration))
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
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I explore", CancellationToken.None))
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
    public async Task HappyPath_ProducesNarrativeStateUpdateAndTurnDone()
    {
        // Arrange
        ArrangeExplorationTurn();

        SetupAgentExecutorStreaming(
            new NarrativeChunk("The dungeon echoes..."),
            new NarrativeChunk(" You see a shadow."),
            new AgentTrace([new ToolCallTrace("Dice.Roll", "{\"expr\":\"d20\"}")], SuppressedNarrative: null));

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "I explore", CancellationToken.None))
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
    public async Task Trace_PersistsTurnDelta_SoEvalsCanSeeWhatActuallyChanged()
    {
        // The turn trace must carry the authoritative delta, not just the narrative — that is what lets
        // offline eval analysis tell a real outcome from a fabricated one. Here pre-state == post-state
        // (same context returned for both loads), so the persisted delta records a no-op: the ground truth
        // that would contradict any narrative claiming silver spent or an item gained.
        ArrangeExplorationTurn();
        SetupAgentExecutorStreaming(
            new NarrativeChunk("You haggle, but the crone waves you off."),
            new AgentTrace([], SuppressedNarrative: null));

        TurnTraceEntity? saved = null;
        _turnTraceRepo
            .Setup(r => r.Save(It.IsAny<TurnTraceEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TurnTraceEntity, CancellationToken>((t, _) => saved = t)
            .Returns(Task.CompletedTask);

        var coordinator = CreateCoordinator();
        await foreach (var _ in coordinator.ExecuteTurnAsync(_sessionId, "I buy the map", CancellationToken.None)) { }

        Assert.NotNull(saved);
        Assert.NotNull(saved!.TurnDeltaJson);
        var delta = JsonSerializer.Deserialize<TurnDelta>(
            saved.TurnDeltaJson!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(delta);
        Assert.True(delta!.IsNoOp);
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
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "attack", CancellationToken.None))
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
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "hello", CancellationToken.None))
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
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "hello", CancellationToken.None))
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
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "continue", CancellationToken.None))
            events.Add(evt);

        // Assert — the client is re-synced to the ended state, then the turn is refused. No narration
        // (the model could fabricate a revival), no TurnDone.
        Assert.Contains(events, e => e is StateUpdate);
        var error = events.OfType<TurnError>().Single();
        Assert.Equal("This story has ended. Begin a new character to continue.", error.Message);
        Assert.DoesNotContain(events, e => e is NarrativeChunk);
        Assert.DoesNotContain(events, e => e is TurnDone);

        // A refused turn produced no trace (the transaction never opened).
        _turnTraceRepo.Verify(
            r => r.Save(It.IsAny<TurnTraceEntity>(), It.IsAny<CancellationToken>()), Times.Never);

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
