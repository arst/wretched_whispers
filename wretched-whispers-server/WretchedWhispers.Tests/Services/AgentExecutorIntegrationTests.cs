using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
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
    private static AgentExecutor CreateExecutor(IChatClient chatClient, IReadOnlyList<ChatMessage>? history = null)
    {
        var historyRepo = new Mock<IChatHistoryRepository>();
        historyRepo
            .Setup(r => r.LoadSession(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history ?? []);

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
    public async Task ProseBeforeATool_IsShown_InOrder_AheadOfTheToolResult()
    {
        var (tools, ctx) = ExplorationToolsAndContext();

        // The model writes, calls a tool, then writes again. Nothing is withheld: the player reads the
        // first passage while the tool runs, and the events arrive in the order things happened.
        var client = new ScriptedChatClient(
            ChatResponses.TextThenToolCall(
                "Something stirs in the muck.", "call_1", "CreateEncounter",
                new()
                {
                    ["name"] = "Grim",
                    ["description"] = "Something moves in the muck",
                    ["initialEncounterType"] = "Hostile"
                }),
            ChatResponses.Text("Grim claws free of the muck, doomed already."));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "Grim");

        Assert.Collection(events,
            e => Assert.IsType<ModelRequest>(e),
            e => Assert.Equal("Something stirs in the muck.", Assert.IsType<NarrativeChunk>(e).Text),
            e => Assert.Equal("CreateEncounter", Assert.IsType<ToolResult>(e).Function),
            e => Assert.Equal("\n\nGrim claws free of the muck, doomed already.", Assert.IsType<NarrativeChunk>(e).Text),
            e => Assert.IsType<AgentTrace>(e));
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
    public async Task ProseAroundSeveralToolRounds_AllSurfaces_AsSeparateParagraphs()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(
            ChatResponses.TextThenToolCall("FIRST passage.", "call_1", "CreateEncounter", new()
            {
                ["name"] = "Grim",
                ["description"] = "Something moves in the muck",
                ["initialEncounterType"] = "Hostile"
            }),
            ChatResponses.TextThenToolCall("SECOND passage.", "call_2", "Roll", new()
            {
                ["diceExpression"] = "d6"
            }),
            ChatResponses.Text("FINAL passage."));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "Grim");

        var narrative = string.Concat(events.OfType<NarrativeChunk>().Select(c => c.Text));
        Assert.Equal("FIRST passage.\n\nSECOND passage.\n\nFINAL passage.", narrative);
        Assert.Equal(new[] { "CreateEncounter", "Roll" }, events.OfType<ToolResult>().Select(t => t.Function));
    }

    [Fact]
    public async Task LongNarration_ArrivesInSeveralPieces_ThatJoinToTheOriginal()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var words = Enumerable.Range(0, 60).Select(i => $"word{i} ").ToArray();
        var client = new ChunkedTextChatClient(words);

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "hello");

        var chunks = events.OfType<NarrativeChunk>().Select(c => c.Text).ToList();
        Assert.True(chunks.Count > 3, $"expected the narration in several pieces, got {chunks.Count}");
        Assert.True(chunks.Count < words.Length / 2, "pieces should be coalesced, not one per model token");
        Assert.Equal(string.Concat(words), string.Concat(chunks));
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
        Assert.Equal(new PromptComposer().VersionHash(ctx), trace.PromptHash);
        Assert.Matches("^[0-9a-f]{64}$", trace.PromptHash!);
    }

    [Fact]
    public async Task Trace_ListsEachModelRequest_WithItsOwnUsageAndFinishReason()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new UpdateScriptChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "Roll", new Dictionary<string, object?> { ["diceExpression"] = "d6" })])
                    { ModelId = "gpt-test" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 100, CachedInputTokenCount = 40, OutputTokenCount = 10, ReasoningTokenCount = 4 })])
                    { ModelId = "gpt-test", FinishReason = ChatFinishReason.ToolCalls },
            ],
            [
                new ChatResponseUpdate(ChatRole.Assistant, "The die clatters.") { ModelId = "gpt-test" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 200, CachedInputTokenCount = 150, OutputTokenCount = 20, ReasoningTokenCount = 8 })])
                    { ModelId = "gpt-test", FinishReason = ChatFinishReason.Stop },
            ]);

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "I roll");

        var calls = events.OfType<AgentTrace>().Single().ModelCalls;
        Assert.NotNull(calls);
        Assert.Collection(calls,
            first =>
            {
                Assert.Equal("gpt-test", first.ModelId);
                Assert.Equal(100, first.InputTokens);
                Assert.Equal(40, first.CachedInputTokens);
                Assert.Equal(10, first.OutputTokens);
                Assert.Equal(4, first.ReasoningTokens);
                Assert.Equal("tool_calls", first.FinishReason);
            },
            second =>
            {
                Assert.Equal(200, second.InputTokens);
                Assert.Equal(150, second.CachedInputTokens);
                Assert.Equal(20, second.OutputTokens);
                Assert.Equal(8, second.ReasoningTokens);
                Assert.Equal("stop", second.FinishReason);
            });
        Assert.All(calls, call =>
        {
            Assert.True(call.DurationMs >= 0);
            Assert.InRange(call.FirstOutputMs ?? -1, 0, call.DurationMs);
        });
    }

    [Fact]
    public async Task ModelId_FallsBackToTheConfiguredModel_WhenTheProviderReportsNone()
    {
        // Azure streams an empty model id on every update; the deployment the client is configured
        // with is then the best record of which model served the turn.
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new UpdateScriptChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "Fog, and nothing else.") { ModelId = "" }])
        {
            Metadata = new ChatClientMetadata("azure", null, "my-deployment")
        };

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "hello");

        var trace = events.OfType<AgentTrace>().Single();
        Assert.Equal("my-deployment", trace.ModelId);
        Assert.Equal("my-deployment", Assert.Single(trace.ModelCalls!).ModelId);
    }

    [Fact]
    public async Task TheRequest_IsEmittedBeforeTheRun_AndIsWhatTheModelWasSent()
    {
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(ChatResponses.Text("The road goes on."));
        List<ChatMessage> history =
        [
            new(ChatRole.User, "begin"),
            new(ChatRole.Assistant, "You wake in a ditch.") { AuthorName = "Game_Master" },
        ];

        var events = await RunAsync(CreateExecutor(client, history), tools, ctx, "I walk on");

        // First, so a turn that fails mid-run has still recorded what it sent.
        var request = JsonNode.Parse(Assert.IsType<ModelRequest>(events[0]).Json)!;
        var (sentMessages, sentOptions) = client.Requests[0];
        Assert.NotNull(sentOptions);
        Assert.Equal(sentOptions.Instructions, request["instructions"]!.GetValue<string>());
        Assert.Equal(
            sentMessages.Select(m => (m.Role.Value, m.Text)),
            request["messages"]!.AsArray().Select(m => (m!["role"]!.GetValue<string>(), m["text"]!.GetValue<string>())));
        Assert.Equal("Game_Master", request["messages"]![1]!["author"]!.GetValue<string>());
        Assert.Equal(
            sentOptions.Tools!.Select(t => t.Name),
            request["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
        var roll = request["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "Roll")!;
        Assert.False(string.IsNullOrWhiteSpace(roll["description"]!.GetValue<string>()));
        Assert.Equal("object", roll["parameters"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolResults_CarryTheCallsArguments_AndWhetherItFailed()
    {
        // The turn ledger is built from these: a refused or unknown call changed nothing and is marked.
        var (tools, ctx) = ExplorationToolsAndContext();
        var client = new ScriptedChatClient(
            ChatResponses.ToolCall("call_1", "Roll", new() { ["diceExpression"] = "banana" }),  // fails validation
            ChatResponses.ToolCall("call_2", "Roll", new() { ["diceExpression"] = "d6" }),
            ChatResponses.ToolCall("call_3", "NoSuchTool", new() { ["x"] = 1 }),
            ChatResponses.Text("The die clatters."));

        var events = await RunAsync(CreateExecutor(client), tools, ctx, "I roll");

        var results = events.OfType<ToolResult>().ToList();
        Assert.Equal(new[] { "Roll", "Roll", "NoSuchTool" }, results.Select(r => r.Function));
        Assert.Equal(new[] { true, false, true }, results.Select(r => r.Failed));
        Assert.Contains("banana", results[0].Arguments);
        Assert.Contains("d6", results[1].Arguments);
    }

    [Fact]
    public void ToolResult_ServerOnlyFields_AreNeverSerializedToTheClient()
    {
        var json = System.Text.Json.JsonSerializer.Serialize<GameTurnEvent>(
            new ToolResult("Roll", "4", """{"diceExpression":"d6"}""", Failed: true),
            System.Text.Json.JsonSerializerOptions.Web);

        Assert.DoesNotContain("arguments", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diceExpression", json);
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

        /// <summary>What each model call was sent, in call order.</summary>
        public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add((messages.ToList(), options));
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

        /// <summary>What the client reports about itself, as a real provider client does.</summary>
        public ChatClientMetadata? Metadata { get; init; }

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

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? Metadata : null;

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
