using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Smoke harness for the Agent Framework executor with a scripted <see cref="IChatClient"/>
/// (no real LLM). This is the regression oracle for the SK→Agent Framework migration: it proves
/// the new agent loop both (a) streams assistant text out as NarrativeChunks and (b) actually
/// auto-invokes a tool, mutating domain state and surfacing a ToolResult.
/// </summary>
public class AgentExecutorIntegrationTests
{
    private static AgentExecutor CreateExecutor(IChatClient chatClient)
    {
        var historyRepo = new Mock<IChatHistoryRepository>();
        historyRepo
            .Setup(r => r.LoadSession(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChatMessage>?)new List<ChatMessage>());

        return new AgentExecutor(
            chatClient,
            historyRepo.Object,
            new ChatHistoryReducer(chatClient, historyRepo.Object, NullLogger<ChatHistoryReducer>.Instance),
            new PromptComposer(),
            NullLogger<AgentExecutor>.Instance);
    }

    private static (AgentToolProvider Provider, ICharactersRepository CharsRepo) CreateToolProvider()
    {
        var services = new ServiceCollection();
        var charsRepo = new Mock<ICharactersRepository>();
        // CreateCharacter saves then the wrapper reads back via the returned DTO; Save is a no-op mock.
        charsRepo.Setup(r => r.Save(It.IsAny<Character>())).Returns(Task.CompletedTask);
        var campsRepo = new Mock<ICampaignsRepository>().Object;
        var encsRepo = new Mock<IEncountersRepository>().Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

        // AgentToolProvider constructs the *Tools classes from these Core services.
        services.AddSingleton(charsRepo.Object);
        services.AddSingleton(campsRepo);
        services.AddSingleton(encsRepo);
        services.AddSingleton(dice);
        services.AddSingleton(new CharacterCreationService(charsRepo.Object, dice));
        services.AddSingleton(new CharacterService(charsRepo.Object, dice));
        services.AddSingleton(new CampaignService(campsRepo, charsRepo.Object, dice));
        services.AddSingleton(new EncounterService(dice, charsRepo.Object, encsRepo));

        var sp = services.BuildServiceProvider();
        return (new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance), charsRepo.Object);
    }

    [Fact]
    public async Task PlainNarrative_IsStreamedAsNarrativeChunks()
    {
        var (provider, _) = CreateToolProvider();
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = provider.GetToolsForStage(ctx, SessionStage.Exploration);

        var client = new ScriptedChatClient(
            ChatResponses.Text("The world rots. A name, wretch?"));

        var executor = CreateExecutor(client);

        var events = new List<GameTurnEvent>();
        await foreach (var evt in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), "hello", CancellationToken.None))
            events.Add(evt);

        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));
        Assert.Contains("wretch", narrative);
    }

    [Fact]
    public async Task ModelToolCall_IsAutoInvoked_MutatesStateAndEmitsToolResult()
    {
        var (provider, _) = CreateToolProvider();
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = provider.GetToolsForStage(ctx, SessionStage.Exploration);

        // First call: model requests CreateEncounter. Second call: model narrates the result.
        var client = new ScriptedChatClient(
            ChatResponses.ToolCall("call_1", "CreateEncounter", new()
            {
                ["name"] = "Grim",
                ["description"] = "Something moves in the muck",
                ["initialEncounterType"] = "Hostile"
            }),
            ChatResponses.Text("Grim claws free of the muck, doomed already."));

        var executor = CreateExecutor(client);

        var events = new List<GameTurnEvent>();
        await foreach (var evt in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), "Grim", CancellationToken.None))
            events.Add(evt);

        // The tool actually ran: the wrapper set the encounter id on the session context.
        Assert.NotNull(ctx.ActiveEncounterId);

        // A ToolResult for CreateEncounter surfaced.
        Assert.Contains(events.OfType<ToolResult>(), t => t.Function == "CreateEncounter");

        // The follow-up narration streamed out.
        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));
        Assert.Contains("Grim", narrative);
    }

    [Fact]
    public async Task PreToolFabrication_IsSuppressed_PostToolNarrationSurvives()
    {
        var (provider, _) = CreateToolProvider();
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = provider.GetToolsForStage(ctx, SessionStage.Exploration);

        // The model fabricates an outcome BEFORE calling the tool, then narrates after.
        var client = new ScriptedChatClient(
            ChatResponses.TextThenToolCall(
                "FABRICATED: you have already triumphed!", "call_1", "CreateEncounter",
                new()
                {
                    ["name"] = "Grim",
                    ["description"] = "Something moves in the muck",
                    ["initialEncounterType"] = "Hostile"
                }),
            ChatResponses.Text("Grim claws free of the muck, doomed already."));

        var executor = CreateExecutor(client);

        var events = new List<GameTurnEvent>();
        await foreach (var evt in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), "Grim", CancellationToken.None))
            events.Add(evt);

        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));

        // Pre-tool fabrication is dropped; only the post-tool narration reaches the player.
        Assert.DoesNotContain("FABRICATED", narrative);
        Assert.Contains("Grim claws free", narrative);
        // The tool still ran.
        Assert.Contains(events.OfType<ToolResult>(), t => t.Function == "CreateEncounter");
    }

    [Fact]
    public async Task TransientFailure_IsNotRetried_ByTheExecutor()
    {
        var (provider, _) = CreateToolProvider();
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = provider.GetToolsForStage(ctx, SessionStage.Exploration);

        var client = new ThrowingChatClient(new HttpRequestException("transient upstream failure"));
        var executor = CreateExecutor(client);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), "Grim", CancellationToken.None))
            {
            }
        });

        // The executor surfaces the transient fault WITHOUT re-running the agent loop. Transient retry
        // is the transport's job (the Azure client), where it retries a single HTTP request without
        // re-executing tools — so a turn can never double-apply a tool via an executor-level retry.
        Assert.Equal(1, client.StreamingCalls);
    }

    // ---- Test doubles ----

    private static class ChatResponses
    {
        public static ChatResponse Text(string text) =>
            new(new ChatMessage(ChatRole.Assistant, text));

        public static ChatResponse ToolCall(string callId, string name, Dictionary<string, object?> args) =>
            new(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent(callId, name, args)
            }));

        // Assistant turn that narrates an outcome BEFORE calling the tool — the pre-tool prose is
        // exactly what the no-fabrication guardrail must suppress.
        public static ChatResponse TextThenToolCall(string text, string callId, string name, Dictionary<string, object?> args) =>
            new(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent(text),
                new FunctionCallContent(callId, name, args)
            }));
    }

    private sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate(message.Role, message.Contents);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    /// <summary>Throws a transient fault on the model call and counts how many times it was invoked.</summary>
    private sealed class ThrowingChatClient(Exception toThrow) : IChatClient
    {
        public int StreamingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw toThrow;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            throw toThrow;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
