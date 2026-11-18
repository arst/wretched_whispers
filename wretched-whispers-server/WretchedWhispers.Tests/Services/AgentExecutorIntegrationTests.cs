using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
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

    private static async Task<List<GameTurnEvent>> RunAsync(AgentExecutor executor, IReadOnlyList<AIFunction> tools,
        SessionContext ctx, string playerMessage)
    {
        var events = new List<GameTurnEvent>();
        await foreach (var evt in executor.ExecuteAsync(tools, ctx, Guid.NewGuid(), playerMessage, CancellationToken.None))
            events.Add(evt);
        return events;
    }

    [Fact]
    public async Task ProseBetweenToolRounds_IsSuppressed_OnlyTheFinalSegmentSurfaces()
    {
        // text -> tool -> text -> tool -> text. The middle prose was written after the first result but
        // before the second, so it could not know the second outcome: only the last segment is trusted.
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(
            ChatResponses.TextThenToolCall("FIRST: the fight is won.", "call_1", "CreateEncounter", new()
            {
                ["name"] = "Grim",
                ["description"] = "Something moves in the muck",
                ["initialEncounterType"] = "Hostile"
            }),
            ChatResponses.TextThenToolCall("SECOND: you roll a six.", "call_2", "Roll", new()
            {
                ["diceExpression"] = "d6"
            }),
            ChatResponses.Text("FINAL: the muck parts."));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "Grim");

        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));
        Assert.Equal("FINAL: the muck parts.", narrative);
        Assert.Equal(new[] { "CreateEncounter", "Roll" }, events.OfType<ToolResult>().Select(t => t.Function));
        var trace = events.OfType<AgentTrace>().Single();
        Assert.Contains("FIRST", trace.SuppressedNarrative);
        Assert.Contains("SECOND", trace.SuppressedNarrative);
    }

    [Fact]
    public async Task EmptyResponseWithNoToolCall_IsRetriedOnce()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(
            ChatResponses.Text(""),
            ChatResponses.Text("The bell tolls. What now, wretch?"));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "hello");

        Assert.Equal(2, client.Calls);
        Assert.Equal("The bell tolls. What now, wretch?",
            string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text)));
    }

    [Fact]
    public async Task EmptyResponseTwice_IsRetriedOnlyOnce_AndYieldsNothing()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(ChatResponses.Text(""), ChatResponses.Text(""));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "hello");

        Assert.Equal(2, client.Calls);
        Assert.Empty(events.OfType<NarrativeChunk>());
        Assert.Empty(events.OfType<ToolResult>());
    }

    [Fact]
    public async Task EmptyResponseAfterAToolRan_IsNotRetried()
    {
        // A tool already changed state: re-running the loop could apply it twice.
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(
            ChatResponses.ToolCall("call_1", "CreateEncounter", new()
            {
                ["name"] = "Grim",
                ["description"] = "Something moves in the muck",
                ["initialEncounterType"] = "Hostile"
            }),
            ChatResponses.Text(""),
            ChatResponses.Text("SHOULD NOT BE REACHED"));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "Grim");

        Assert.Equal(2, client.Calls);
        Assert.Empty(events.OfType<NarrativeChunk>());
        Assert.Single(events.OfType<ToolResult>(), t => t.Function == "CreateEncounter");
    }

    [Fact]
    public async Task Trace_CarriesModelIdUsageIterationsAndPromptHash()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new UpdateScriptChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "Roll", new Dictionary<string, object?> { ["diceExpression"] = "d6" })])
                    { ModelId = "gpt-test" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 })]),
            ],
            [
                new ChatResponseUpdate(ChatRole.Assistant, "The die clatters.") { ModelId = "gpt-test" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 200, OutputTokenCount = 20 })]),
            ]);

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "I roll");

        var trace = events.OfType<AgentTrace>().Single();
        Assert.Equal("gpt-test", trace.ModelId);
        Assert.Equal(300, trace.InputTokens);
        Assert.Equal(30, trace.OutputTokens);
        Assert.Equal(2, trace.Iterations);
        Assert.Equal(AgentExecutor.HashPrompt(new PromptComposer().Compose(ctx)), trace.PromptHash);
        Assert.Matches("^[0-9a-f]{64}$", trace.PromptHash!);
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

        /// <summary>How many model calls were made.</summary>
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
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

    /// <summary>Streams a fixed list of raw updates per model call — for model ids and usage content.</summary>
    private sealed class UpdateScriptChatClient(params ChatResponseUpdate[][] calls) : IChatClient
    {
        private readonly Queue<ChatResponseUpdate[]> _calls = new(calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in _calls.Dequeue())
                yield return update;
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
