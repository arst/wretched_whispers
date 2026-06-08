using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Semantic;

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
/// </summary>
public sealed class AgentExecutor(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
    ChatHistoryReducer historyReducer,
    PromptComposer promptComposer,
    ResiliencePipelineProvider<string> resilienceProvider,
    ILogger<AgentExecutor> logger) : IAgentExecutor
{
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
        // Bound the model's context on long sessions (summarize older messages). Done once here
        // rather than inside the retry pipeline so a retried run doesn't re-summarize.
        history = await historyReducer.ReduceAsync(history, ct);
        var agent = CreateAgent(tools, sessionContext);

        var pipeline = resilienceProvider.GetPipeline("llm-retry");
        var preToolNarrative = new List<string>();
        var postToolNarrative = new List<string>();
        var toolResults = new List<ToolResult>();
        var sawTool = false;

        await pipeline.ExecuteAsync(async token =>
        {
            preToolNarrative.Clear();
            postToolNarrative.Clear();
            toolResults.Clear();
            sawTool = false;

            var messages = new List<ChatMessage>(history) { new(ChatRole.User, playerMessage) };
            var session = await agent.CreateSessionAsync(token);

            // CallId -> function name, so a FunctionResultContent can be attributed to its call.
            var callNames = new Dictionary<string, string>();

            await foreach (var update in agent.RunStreamingAsync(messages, session, null, token))
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
                            break;
                        case FunctionResultContent result:
                            sawTool = true;
                            var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                                ? n
                                : "unknown";
                            toolResults.Add(new ToolResult(name, result.Result?.ToString() ?? ""));
                            break;
                    }
                }
            }
        }, ct);

        // Tools-authoritative: when tools ran, trust only post-tool narration; drop pre-tool prose.
        var narrative = sawTool ? postToolNarrative : preToolNarrative;

        if (sawTool && preToolNarrative.Count > 0)
            logger.LogWarning(
                "Suppressed {Count} pre-tool narrative chunk(s) as potential fabrication",
                preToolNarrative.Count);

        logger.LogInformation(
            "Agent execution complete — {ChunkCount} narrative chunks ({Suppressed} suppressed), {ToolCount} tool results",
            narrative.Count, sawTool ? preToolNarrative.Count : 0, toolResults.Count);

        foreach (var text in narrative)
            yield return new NarrativeChunk(text);

        foreach (var toolResult in toolResults)
            yield return toolResult;
    }

    private AIAgent CreateAgent(IReadOnlyList<AIFunction> tools, SessionContext sessionContext) =>
        new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Game_Master",
            ChatOptions = new ChatOptions
            {
                Instructions = promptComposer.Compose(sessionContext),
                Tools = tools.Cast<AITool>().ToList()
            }
        });
}
