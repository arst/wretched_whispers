using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Drives a single game-master turn on Microsoft Agent Framework: loads prior history, builds a
/// <see cref="ChatClientAgent"/> scoped to the stage's tools, streams the run, and maps streamed
/// content to <see cref="GameTurnEvent"/>s (narrative text + tool results).
///
/// Tools-authoritative guardrail (no fabricated outcomes): if the model calls any tool this turn,
/// only narration emitted AFTER the first tool call is trusted and surfaced to the player. Prose
/// emitted before any tool call is discarded as potential fabrication (the model describing an
/// outcome it has not actually resolved). Turns with no tool calls are conversational, so their
/// prose passes through unchanged.
///
/// Resilience note: the agent run is deliberately NOT wrapped in a retry here. The function-invocation
/// loop executes tools that mutate domain state inside the turn's open transaction, so re-running the
/// whole loop on a transient fault would double-apply those tools (e.g. create two characters).
/// Transient HTTP retries are the transport's job — the Azure OpenAI client retries an individual
/// model request without re-executing any tools whose results are already in the conversation.
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
    private readonly IChatClient _functionInvokingClient = chatClient
        .AsBuilder()
        .UseFunctionInvocation(configure: c =>
        {
            c.IncludeDetailedErrors = true;
            c.MaximumConsecutiveErrorsPerRequest = 3;
            // Hard ceiling on tool-call iterations per turn — bounds a runaway (but non-erroring) loop.
            c.MaximumIterationsPerRequest = 15;
        })
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
        var agent = CreateAgent(tools, sessionContext);

        var preToolNarrative = new List<string>();
        var postToolNarrative = new List<string>();
        var toolResults = new List<ToolResult>();
        var sawTool = false;

        var messages = new List<ChatMessage>(history) { new(ChatRole.User, playerMessage) };

        // Stateless run: we own the full conversation (loaded + summarized from our own store and
        // passed in `messages`), so we pass no MAF session/thread rather than calling CreateSessionAsync
        // for a fresh thread we never persist through.
        var callNames = new Dictionary<string, string>();

        await foreach (var update in agent.RunStreamingAsync(messages, session: null, options: null, ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        (sawTool ? postToolNarrative : preToolNarrative).Add(text.Text);
                        break;
                    case FunctionCallContent call:
                        sawTool = true;
                        if (call.CallId is not null) callNames[call.CallId] = call.Name;
                        // Durable, greppable audit of every tool invocation (args + result below). This is
                        // the only always-on record that a roll/round was actually resolved by the domain —
                        // the DB stores narrative text only, and OTel spans need a collector attached.
                        logger.LogInformation("Tool call: {Tool}({Args})", call.Name, call.Arguments);
                        break;
                    case FunctionResultContent result:
                        sawTool = true;
                        var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                            ? n
                            : "unknown";
                        toolResults.Add(new ToolResult(name, result.Result?.ToString() ?? ""));
                        logger.LogInformation("Tool result: {Tool} -> {Result}", name, result.Result);
                        break;
                }
            }
        }

        // Tools-authoritative: when tools ran, trust only post-tool narration; drop pre-tool prose.
        var narrative = sawTool ? postToolNarrative : preToolNarrative;

        if (sawTool && preToolNarrative.Count > 0)
            logger.LogWarning(
                "Suppressed {Count} pre-tool narrative chunk(s) as potential fabrication",
                preToolNarrative.Count);

        // Output guardrail: strip any raw entity GUIDs the model leaked into the prose. The narrative
        // is already buffered for the fabrication guardrail, so scrub the joined text (a leaked GUID
        // is split across streamed sub-word chunks and would not match within a single chunk).
        var scrubbed = OutputScrubber.RedactGuids(string.Concat(narrative), out var redacted);

        logger.LogInformation(
            "Agent execution complete — {ChunkCount} narrative chunks ({Suppressed} suppressed), {ToolCount} tool results, ids redacted: {Redacted}",
            narrative.Count, sawTool ? preToolNarrative.Count : 0, toolResults.Count, redacted);

        if (scrubbed.Length > 0)
            yield return new NarrativeChunk(scrubbed);

        foreach (var toolResult in toolResults)
            yield return toolResult;
    }

    private AIAgent CreateAgent(IReadOnlyList<AIFunction> tools, SessionContext sessionContext)
    {
        var agent = new ChatClientAgent(_functionInvokingClient, new ChatClientAgentOptions
        {
            Name = "Game_Master",
            // We already wrapped the client with function invocation above; don't let the agent
            // wrap it again (which would double-invoke every tool).
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = promptComposer.Compose(sessionContext),
                Tools = tools.Cast<AITool>().ToList()
            }
        });

        return agent;
    }
}
