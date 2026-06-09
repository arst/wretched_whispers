using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Infrastructure.Persistence;
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
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public TurnCoordinatorTests()
    {
        // A no-op unit of work: BeginAsync hands back a scope whose CommitAsync/DisposeAsync do nothing.
        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork
            .Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope.Object);
    }

    private TurnCoordinator CreateCoordinator() =>
        new(
            _contextLoader.Object,
            _toolProvider.Object,
            _agentExecutor.Object,
            _chatHistoryRepo.Object,
            _unitOfWork.Object,
            NullLogger<TurnCoordinator>.Instance);

    private SessionContext MakeExplorationContext()
    {
        var ctx = new SessionContext { SessionId = _sessionId };
        var campaign = Core.Campaigns.Campaign.Create(
            Core.Dices.DiceExpr.Parse("d6"), "Test Campaign", "A test");
        var characterId = Guid.NewGuid();
        campaign.JoinGame(characterId);
        campaign.Start();
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
    public async Task HappyPath_ProducesNarrativeStateUpdateAndTurnDone()
    {
        // Arrange
        SetupChatSession();

        var context = MakeExplorationContext();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        SetupToolsForExploration();

        SetupAgentExecutorStreaming(
            new NarrativeChunk("The dungeon echoes..."),
            new NarrativeChunk(" You see a shadow."));

        _chatHistoryRepo
            .Setup(r => r.SaveMessage(
                _chatSessionId,
                It.IsAny<ChatMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

        _contextLoader.Verify(
            l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()),
            Times.Exactly(2)); // once for context, once post-turn reload
    }

    [Fact]
    public async Task AgentExecutorThrows_ProducesTurnError()
    {
        // Arrange
        SetupChatSession();

        var context = MakeExplorationContext();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        SetupToolsForExploration();

        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                _chatSessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(new InvalidOperationException("LLM exploded")));

        _chatHistoryRepo
            .Setup(r => r.SaveMessage(
                _chatSessionId,
                It.IsAny<ChatMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "attack", CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        Assert.Contains(events, e => e is TurnError);
        var error = events.OfType<TurnError>().First();
        Assert.Equal("An error occurred while processing your action", error.Message);

        // No TurnDone after error
        Assert.DoesNotContain(events, e => e is TurnDone);
    }

    [Fact]
    public async Task ConcurrencyConflict_ProducesRetryTurnError()
    {
        // Arrange — the agent run throws DbUpdateConcurrencyException (another turn committed first).
        SetupChatSession();

        var context = MakeExplorationContext();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        SetupToolsForExploration();

        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                _chatSessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(new DbUpdateConcurrencyException("conflict")));

        _chatHistoryRepo
            .Setup(r => r.SaveMessage(_chatSessionId, It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "attack", CancellationToken.None))
            events.Add(evt);

        // Assert — the dedicated retry message, and no TurnDone.
        var error = events.OfType<TurnError>().First();
        Assert.Equal("This session was updated by another action. Please retry.", error.Message);
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

    private static async IAsyncEnumerable<GameTurnEvent> ThrowingAsyncEnumerable(Exception ex)
    {
        await Task.CompletedTask;
        throw ex;
#pragma warning disable CS0162 // Unreachable code — needed so compiler treats this as IAsyncEnumerable
        yield break;
#pragma warning restore CS0162
    }
}
