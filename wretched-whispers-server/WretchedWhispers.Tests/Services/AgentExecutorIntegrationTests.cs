using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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

    private static AgentToolProvider CreateToolProvider()
    {
        var charsRepo = new Mock<ICharactersRepository>().Object;
        var campsRepo = new Mock<ICampaignsRepository>().Object;
        var encsRepo = new Mock<IEncountersRepository>().Object;
        var dice = new Dice(new Mock<IRandomService>().Object);

        return new AgentToolProvider(
            charsRepo,
            encsRepo,
            new CharacterService(charsRepo, dice),
            new CampaignService(campsRepo, charsRepo, dice),
            new EncounterService(dice, charsRepo, encsRepo),
            dice,
            NullLogger<AgentToolProvider>.Instance);
    }

    /// <summary>The shared arrange: a fresh session context and the Exploration-stage tool set.</summary>
    private static (IReadOnlyList<AIFunction> Tools, SessionContext Ctx) ExplorationToolsAndContext()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        var (tools, _) = CreateToolProvider().GetToolsForStage(ctx, SessionStage.Exploration);
        return (tools, ctx);
    }

    [Fact]
    public async Task PlainNarrative_IsStreamedAsNarrativeChunks()
    {
        var (tools, ctx) = ExplorationToolsAndContext();

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
        var (tools, ctx) = ExplorationToolsAndContext();

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
        var (tools, ctx) = ExplorationToolsAndContext();

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
    public async Task GuidSplitAcrossStreamedChunks_IsStillScrubbed()
    {
        var (tools, ctx) = ExplorationToolsAndContext();

        // The GUID arrives split across two streamed updates, so no single chunk matches the GUID
        // pattern — only the buffered, joined narrative does. This pins the executor's buffering:
        // scrubbing per-chunk would let the leaked id through.
        var client = new ChunkedTextChatClient(
            "The sludge rat 7504b8e9-3c59-",
            "47b6-b38b-96c0bb5f30bd lunges at you.");
        var executor = CreateExecutor(client);

        var events = new List<GameTurnEvent>();
        await foreach (var evt in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), "hello", CancellationToken.None))
            events.Add(evt);

        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));
        Assert.DoesNotMatch(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", narrative);
        Assert.DoesNotContain("7504b8e9", narrative);
        Assert.Contains("The sludge rat", narrative);
        Assert.Contains("lunges at you.", narrative);
    }

    [Fact]
    public async Task TransientFailure_IsNotRetried_ByTheExecutor()
    {
        var (tools, ctx) = ExplorationToolsAndContext();

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

    /// <summary>Streams one assistant message as several raw text updates — a chunk boundary can fall
    /// mid-GUID, which <see cref="ScriptedChatClient"/> (one update per message) cannot express.</summary>
    private sealed class ChunkedTextChatClient(params string[] chunks) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.CompletedTask;
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
