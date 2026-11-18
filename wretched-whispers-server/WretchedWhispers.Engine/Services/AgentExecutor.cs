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
/// <see cref="ChatClientAgent"/> scoped to the stage's tools, streams the run, and maps streamed
/// content to <see cref="GameTurnEvent"/>s (narrative text + tool results).
///
/// Tools-authoritative guardrail (no fabricated outcomes): narration is split into segments, a new
/// segment starting at every tool call. Only the LAST segment — written after every tool result was
/// known — is surfaced to the player. Earlier segments described outcomes the model could not yet
/// have known and are recorded as suppressed narrative in the turn trace. Turns with no tool calls
/// are conversational: one segment, passed through unchanged.
///
/// Resilience note: a run that has executed a tool is never retried here. The function-invocation
/// loop executes tools that mutate domain state inside the turn's open transaction, so re-running the
/// loop would double-apply them (e.g. charge for an item twice). Transient HTTP retries are the
/// transport's job — the Azure OpenAI client retries an individual model request without re-executing
/// any tools whose results are already in the conversation. The one executor-level retry is for a run
/// that produced NOTHING — no tool call and no text (an empty completion): no state changed, so
/// running it once more is safe, and it saves the player from a failed turn.
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

        var run = await RunOnceAsync(agent, messages, ct);
        if (run.IsEmpty)
        {
            // Nothing ran and nothing was said: no state changed, so one more attempt is safe.
            logger.LogWarning("Empty model response with no tool calls; retrying the run once");
            run = await RunOnceAsync(agent, messages, ct);
        }

        // Tools-authoritative: surface only the narration written after the last tool call.
        var narrative = run.Segments[^1];
        var suppressedSegments = run.Segments.Take(run.Segments.Count - 1).Where(t => t.Length > 0).ToList();

        if (suppressedSegments.Count > 0)
            logger.LogWarning(
                "Suppressed {Count} narrative segment(s) written before a tool result was known",
                suppressedSegments.Count);

        // Output guardrail: strip any raw entity GUIDs the model leaked into the prose. The narrative
        // is already buffered for the fabrication guardrail, so scrub the joined text (a leaked GUID
        // is split across streamed sub-word chunks and would not match within a single chunk).
        var scrubbed = OutputScrubber.RedactGuids(narrative, out var redacted);

        logger.LogInformation(
            "Agent execution complete — {SegmentCount} narrative segments ({Suppressed} suppressed), {ToolCount} tool results, ids redacted: {Redacted}",
            run.Segments.Count, suppressedSegments.Count, run.ToolResults.Count, redacted);

        if (scrubbed.Length > 0)
            yield return new NarrativeChunk(scrubbed);

        foreach (var toolResult in run.ToolResults)
            yield return toolResult;

        // Out-of-band trace (last): carries the tool-call args and suppressed prose the player-facing
        // events don't expose, so the TurnCoordinator can persist a complete turn trace. Not streamed.
        var suppressed = suppressedSegments.Count > 0 ? string.Join("\n\n", suppressedSegments) : null;
        yield return new AgentTrace(
            run.ToolCalls, suppressed,
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

    /// <summary>What one agent run produced. <see cref="Segments"/> always has at least one entry: the
    /// narration before the first tool call, then one more per tool call.</summary>
    private sealed record RunResult(
        IReadOnlyList<string> Segments, IReadOnlyList<ToolCallTrace> ToolCalls, IReadOnlyList<ToolResult> ToolResults)
    {
        public bool IsEmpty => ToolCalls.Count == 0 && ToolResults.Count == 0 && Segments.All(t => t.Length == 0);
    }

    private async Task<RunResult> RunOnceAsync(AIAgent agent, List<ChatMessage> messages, CancellationToken ct)
    {
        var segments = new List<System.Text.StringBuilder> { new() };
        var toolResults = new List<ToolResult>();
        var toolCalls = new List<ToolCallTrace>();
        var callNames = new Dictionary<string, string>();

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
                        segments[^1].Append(text.Text);
                        break;
                    case FunctionCallContent call:
                        // Anything written from here on may know this call's result; anything before
                        // could not.
                        segments.Add(new System.Text.StringBuilder());
                        if (call.CallId is not null) callNames[call.CallId] = call.Name;
                        // Capture name + args for the turn trace (offline error analysis). Args are the
                        // valuable part — e.g. which itemDescription or dice expr the model passed.
                        toolCalls.Add(new ToolCallTrace(call.Name, SerializeArgs(call.Arguments)));
                        // Debug, not Information: args are model-generated and carry player-derived free
                        // text, which must not land in central logs by default. The durable always-on
                        // record of every invocation is the turn trace the coordinator persists.
                        logger.LogDebug("Tool call: {Tool}({Args})", call.Name, call.Arguments);
                        break;
                    case FunctionResultContent result:
                        var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                            ? n
                            : "unknown";
                        toolResults.Add(new ToolResult(name, result.Result?.ToString() ?? ""));
                        logger.LogDebug("Tool result: {Tool} -> {Result}", name, result.Result);
                        break;
                }
            }
        }

        return new RunResult(segments.Select(b => b.ToString()).ToList(), toolCalls, toolResults);
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
