using System.Runtime.CompilerServices;
using System.Text.Json;
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
        // Bound the model's context on long sessions (summarize older messages).
        history = await historyReducer.ReduceAsync(chatSessionId, history, ct);
        var instructions = promptComposer.Compose(sessionContext);
        var agent = CreateAgent(tools, instructions);
        var messages = new List<ChatMessage>(history) { new(ChatRole.User, playerMessage) };
        _metrics.Reset();

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
            PromptHash: HashPrompt(instructions),
            InputTokens: _metrics.InputTokens,
            OutputTokens: _metrics.OutputTokens,
            Iterations: _metrics.Requests);
    }

    /// <summary>SHA-256 of the composed instructions: equal hashes mean the model saw the same prompt.</summary>
    public static string HashPrompt(string instructions) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(instructions)));

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
        var callNames = new Dictionary<string, string>();
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
                        if (call.CallId is not null) callNames[call.CallId] = call.Name;
                        // Capture name + args for the turn trace (offline error analysis). Args are the
                        // valuable part — e.g. which itemDescription or dice expr the model passed.
                        run.ToolCalls.Add(new ToolCallTrace(call.Name, SerializeArgs(call.Arguments)));
                        // Debug, not Information: args are model-generated and carry player-derived free
                        // text, which must not land in central logs by default. The durable always-on
                        // record of every invocation is the turn trace the coordinator persists.
                        logger.LogDebug("Tool call: {Tool}({Args})", call.Name, call.Arguments);
                        break;
                    case FunctionResultContent result:
                        var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                            ? n
                            : "unknown";
                        run.ToolResults++;
                        logger.LogDebug("Tool result: {Tool} -> {Result}", name, result.Result);
                        yield return new ToolResult(name, result.Result?.ToString() ?? "");
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

/// <summary>Per-turn model-request metrics, filled by <see cref="MetricsCapturingChatClient"/>.</summary>
internal sealed class ModelCallMetrics
{
    public int Requests { get; private set; }
    public string? ModelId { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }

    public void Reset()
    {
        Requests = 0;
        ModelId = null;
        InputTokens = null;
        OutputTokens = null;
    }

    public void CountRequest() => Requests++;

    public void Observe(ChatResponseUpdate update)
    {
        if (!string.IsNullOrEmpty(update.ModelId))
            ModelId = update.ModelId;
        foreach (var usage in update.Contents.OfType<UsageContent>())
        {
            if (usage.Details.InputTokenCount is { } input) InputTokens = (InputTokens ?? 0) + input;
            if (usage.Details.OutputTokenCount is { } output) OutputTokens = (OutputTokens ?? 0) + output;
        }
    }
}

/// <summary>Records each model request's model id and token usage as it streams past.</summary>
internal sealed class MetricsCapturingChatClient(IChatClient inner, ModelCallMetrics metrics) : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        metrics.CountRequest();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            metrics.Observe(update);
            yield return update;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        metrics.CountRequest();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            metrics.Observe(update);
        return response;
    }
}
