using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Drives a single game-master turn on Microsoft Agent Framework: loads prior history, builds a
/// <see cref="ChatClientAgent"/> scoped to the stage's tools, and streams the run as
/// <see cref="GameTurnEvent"/>s AS IT HAPPENS: narration in order as the model writes it (in small
/// pieces, with leaked ids scrubbed), and each tool result when its tool finishes.
///
/// Nothing the model writes is withheld. Prose is not the source of truth: state changes only through
/// tools, and the turn's authoritative outcome is the delta computed from committed state. Whether the
/// model narrates outcomes it has not resolved is measured by the evals, not filtered here.
///
/// Resilience note: a run that has executed a tool is never retried here. The function-invocation
/// loop executes tools that mutate domain state inside the turn's open transaction, so re-running the
/// loop would double-apply them (e.g. charge for an item twice). Transient HTTP retries are the
/// transport's job — the Azure OpenAI client retries an individual model request without re-executing
/// any tools whose results are already in the conversation. The one executor-level retry is for a run
/// that produced NOTHING — no tool call and no text (an empty completion): no state changed and
/// nothing was shown, so running it once more is safe, and it saves the player from a failed turn.
/// </summary>
public sealed class AgentExecutor(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
    ChatHistoryReducer historyReducer,
    PromptComposer promptComposer,
    ILogger<AgentExecutor> logger) : IAgentExecutor
{
    // Drive function calling ourselves so we can surface tool-validation messages back to the model:
    // IncludeDetailedErrors lets the model see WHY a tool call failed (e.g. "quantity must be >= 1")
    // and retry with corrected arguments instead of failing the whole turn.
    private readonly ModelCallMetrics _metrics = new();

    private IChatClient? _functionInvokingClient;

    // Built lazily so the metrics capture can sit BELOW function invocation: it then sees each model
    // request exactly once (one per tool round), with the provider's own model id and usage.
    private IChatClient FunctionInvokingClient => _functionInvokingClient ??= chatClient
        .AsBuilder()
        .UseFunctionInvocation(configure: c =>
        {
            c.IncludeDetailedErrors = true;
            c.MaximumConsecutiveErrorsPerRequest = 3;
            // Hard ceiling on tool-call iterations per turn — bounds a runaway (but non-erroring) loop.
            c.MaximumIterationsPerRequest = 15;
        })
        .Use(inner => new MetricsCapturingChatClient(inner, _metrics))
        .Build();
    public async IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        IReadOnlyList<AIFunction> tools,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = AgentToolProvider.ActivitySource.StartActivity("AgentExecutor.ExecuteAsync");
        activity?.SetTag("session.chat_id", chatSessionId.ToString());

        var history = await chatHistoryRepository.LoadSession(chatSessionId, ct) ?? [];
        // Bound the model's context on long sessions: the stored summary stands in for the messages
        // it covers. Summarizing itself happens after the turn (see TurnCoordinator), never here.
        history = await historyReducer.ComposeAsync(chatSessionId, history, ct);
        var instructions = promptComposer.Compose(sessionContext);
        // Taken now: the turn's tools may move the stage before the trace is written.
        var promptVersion = promptComposer.VersionHash(sessionContext);
        var agent = CreateAgent(tools, instructions);
        var messages = new List<ChatMessage>(history) { new(ChatRole.User, playerMessage) };
        _metrics.Reset();

        // Out-of-band and first: what this turn sends the model, recorded before anything can fail.
        yield return new ModelRequest(DescribeRequest(instructions, messages, tools));

        var run = new RunState();
        await foreach (var evt in RunOnceAsync(agent, messages, run, ct))
            yield return evt;

        if (run.IsEmpty)
        {
            // Nothing ran and nothing was said: no state changed, so one more attempt is safe.
            logger.LogWarning("Empty model response with no tool calls; retrying the run once");
            run = new RunState();
            await foreach (var evt in RunOnceAsync(agent, messages, run, ct))
                yield return evt;
        }

        logger.LogInformation(
            "Agent execution complete — {Chars} narrative chars, {ToolCount} tool results, ids redacted: {Redacted}",
            run.NarrativeChars, run.ToolResults, run.Redacted);

        // Out-of-band trace (last): carries what the player-facing events don't expose, so the
        // TurnCoordinator can persist a complete turn trace. Not streamed.
        yield return new AgentTrace(
            run.ToolCalls,
            ModelId: _metrics.ModelId,
            PromptHash: promptVersion,
            InputTokens: _metrics.InputTokens,
            OutputTokens: _metrics.OutputTokens,
            Iterations: _metrics.Requests,
            ModelCalls: _metrics.Calls.ToList());
    }

    // Relaxed escaping: the prompts are full of dashes and quotes, and this is stored, never rendered.
    private static readonly JsonSerializerOptions RequestJson =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The turn's input as JSON: instructions, the messages (history as composed for the model, then
    /// the player's message) and the tool declarations with their descriptions and parameter schemas
    /// — the three things the model is sent, and so the three that decide how it answers.
    /// </summary>
    private static string DescribeRequest(
        string instructions, IReadOnlyList<ChatMessage> messages, IReadOnlyList<AIFunction> tools)
    {
        var messageArray = new JsonArray();
        foreach (var message in messages)
        {
            var entry = new JsonObject { ["role"] = message.Role.Value };
            if (message.AuthorName is not null) entry["author"] = message.AuthorName;
            entry["text"] = message.Text;
            messageArray.Add(entry);
        }

        var toolArray = new JsonArray();
        foreach (var tool in tools)
            toolArray.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText())
            });

        return new JsonObject
        {
            ["instructions"] = instructions,
            ["messages"] = messageArray,
            ["tools"] = toolArray
        }.ToJsonString(RequestJson);
    }

    /// <summary>What one agent run has produced so far.</summary>
    private sealed class RunState
    {
        public List<ToolCallTrace> ToolCalls { get; } = [];
        public int ToolResults { get; set; }
        public int NarrativeChars { get; set; }
        public bool Redacted { get; set; }
        public bool IsEmpty => ToolCalls.Count == 0 && ToolResults == 0 && NarrativeChars == 0;
    }

    private async IAsyncEnumerable<GameTurnEvent> RunOnceAsync(
        AIAgent agent, List<ChatMessage> messages, RunState run, [EnumeratorCancellation] CancellationToken ct)
    {
        var scrubber = new StreamingScrubber();
        var calls = new Dictionary<string, ToolCallTrace>();
        // Prose on either side of a tool round is separate paragraphs, however the model spaced it.
        var breakBeforeNextText = false;

        NarrativeChunk? Narrate(string text)
        {
            if (text.Length == 0) return null;
            if (breakBeforeNextText)
            {
                text = "\n\n" + text.TrimStart();
                breakBeforeNextText = false;
            }
            run.NarrativeChars += text.Length;
            return new NarrativeChunk(text);
        }

        // Stateless run: we own the full conversation (loaded + summarized from our own store and
        // passed in `messages`), so we pass no MAF session/thread rather than calling CreateSessionAsync
        // for a fresh thread we never persist through.
        await foreach (var update in agent.RunStreamingAsync(messages, session: null, options: null, ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        if (Narrate(scrubber.Append(text.Text)) is { } chunk)
                            yield return chunk;
                        break;
                    case FunctionCallContent call:
                        // The model stops writing while its tool runs: show what it has written so far.
                        if (Narrate(scrubber.Flush()) is { } beforeTool)
                            yield return beforeTool;
                        breakBeforeNextText = run.NarrativeChars > 0;
                        // Capture name + args for the turn trace (offline error analysis) and the turn
                        // ledger. Args are the valuable part — e.g. which itemDescription or dice expr
                        // the model passed.
                        var trace = new ToolCallTrace(call.Name, SerializeArgs(call.Arguments));
                        if (call.CallId is not null) calls[call.CallId] = trace;
                        run.ToolCalls.Add(trace);
                        // Debug, not Information: args are model-generated and carry player-derived free
                        // text, which must not land in central logs by default. The durable always-on
                        // record of every invocation is the turn trace the coordinator persists.
                        logger.LogDebug("Tool call: {Tool}({Args})", call.Name, call.Arguments);
                        break;
                    case FunctionResultContent result:
                        var made = result.CallId is not null && calls.TryGetValue(result.CallId, out var c) ? c : null;
                        var name = made?.Name ?? "unknown";
                        var output = result.Result?.ToString() ?? "";
                        run.ToolResults++;
                        logger.LogDebug("Tool result: {Tool} -> {Result}", name, result.Result);
                        // A tool that threw (validation, a per-turn limit, the turn having ended) or
                        // that does not exist comes back as an error result: nothing was changed.
                        var failed = result.Exception is not null
                            || output.StartsWith("Error: ", StringComparison.Ordinal);
                        yield return new ToolResult(name, output, made?.Arguments, failed);
                        break;
                }
            }
        }

        if (Narrate(scrubber.Flush()) is { } rest)
            yield return rest;
        run.Redacted = scrubber.Redacted;
    }

    private static string? SerializeArgs(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return null;

        try
        {
            return JsonSerializer.Serialize(arguments);
        }
        catch (NotSupportedException)
        {
            // A non-serializable arg value must never break a turn — trace fidelity is best-effort.
            return null;
        }
    }

    private AIAgent CreateAgent(IReadOnlyList<AIFunction> tools, string instructions)
    {
        var agent = new ChatClientAgent(FunctionInvokingClient, new ChatClientAgentOptions
        {
            Name = "Game_Master",
            // We already wrapped the client with function invocation above; don't let the agent
            // wrap it again (which would double-invoke every tool).
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools.Cast<AITool>().ToList()
            }
        });

        return agent;
    }
}

/// <summary>The model requests of one turn, filled by <see cref="MetricsCapturingChatClient"/>.</summary>
internal sealed class ModelCallMetrics
{
    private readonly List<ModelCall> _calls = [];

    /// <summary>One entry per model request, in order.</summary>
    public IReadOnlyList<ModelCall> Calls => _calls;

    public int Requests => _calls.Count;
    public string? ModelId => _calls.LastOrDefault(call => call.ModelId is not null)?.ModelId;
    public long? InputTokens => Sum(call => call.InputTokens);
    public long? OutputTokens => Sum(call => call.OutputTokens);

    public void Reset() => _calls.Clear();

    public void Add(ModelCall call) => _calls.Add(call);

    // Null, not zero, when no request reported the count at all.
    private long? Sum(Func<ModelCall, long?> count) =>
        _calls.Any(call => count(call) is not null) ? _calls.Sum(call => count(call) ?? 0) : null;
}

/// <summary>Watches one model request stream past and sums it up as a <see cref="ModelCall"/>.</summary>
internal sealed class ModelCallObserver(string? configuredModelId)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private string? _modelId;
    private string? _finishReason;
    private long? _inputTokens, _cachedInputTokens, _outputTokens, _reasoningTokens, _firstOutputMs;

    public void Observe(ChatResponseUpdate update)
    {
        if (!string.IsNullOrEmpty(update.ModelId)) _modelId = update.ModelId;
        if (update.FinishReason is { } reason) _finishReason = reason.Value;

        foreach (var content in update.Contents)
        {
            if (content is UsageContent { Details: var usage })
            {
                Add(ref _inputTokens, usage.InputTokenCount);
                Add(ref _cachedInputTokens, usage.CachedInputTokenCount);
                Add(ref _outputTokens, usage.OutputTokenCount);
                Add(ref _reasoningTokens, usage.ReasoningTokenCount);
            }
            else if (content is TextContent { Text.Length: > 0 } or FunctionCallContent)
            {
                _firstOutputMs ??= ElapsedMs();
            }
        }
    }

    // Azure streams an empty model id on every update, so the model the client is configured with
    // (the deployment) stands in whenever the provider names none.
    public ModelCall Complete() => new(
        _modelId ?? configuredModelId, _inputTokens, _cachedInputTokens, _outputTokens, _reasoningTokens,
        ElapsedMs(), _firstOutputMs, _finishReason);

    private long ElapsedMs() => (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    private static void Add(ref long? total, long? count)
    {
        if (count is { } value) total = (total ?? 0) + value;
    }
}

/// <summary>Records each model request — model, usage, timing — as it streams past.</summary>
internal sealed class MetricsCapturingChatClient(IChatClient inner, ModelCallMetrics metrics) : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = NewObserver();
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                call.Observe(update);
                yield return update;
            }
        }
        finally
        {
            // A request that failed part-way still happened, and still cost what it had streamed.
            metrics.Add(call.Complete());
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var call = NewObserver();
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                call.Observe(update);
            return response;
        }
        finally
        {
            metrics.Add(call.Complete());
        }
    }

    // Asked per request: the standalone client can be pointed at another model while the app runs.
    private ModelCallObserver NewObserver() =>
        new(InnerClient.GetService<ChatClientMetadata>()?.DefaultModelId);
}
