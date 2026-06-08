using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Drives a single non-combat game-master turn on Microsoft Agent Framework: loads prior history,
/// builds a <see cref="ChatClientAgent"/> scoped to the stage's tools, streams the run, and maps
/// streamed content to <see cref="GameTurnEvent"/>s (narrative text + tool results).
/// </summary>
public sealed class AgentExecutor(
    IChatClient chatClient,
    IChatHistoryRepository chatHistoryRepository,
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
        var agent = CreateAgent(tools, sessionContext);

        var pipeline = resilienceProvider.GetPipeline("llm-retry");
        var narrativeChunks = new List<NarrativeChunk>();
        var toolResults = new List<ToolResult>();

        await pipeline.ExecuteAsync(async token =>
        {
            narrativeChunks.Clear();
            toolResults.Clear();

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
                            narrativeChunks.Add(new NarrativeChunk(text.Text));
                            break;
                        case FunctionCallContent call when call.CallId is not null:
                            callNames[call.CallId] = call.Name;
                            break;
                        case FunctionResultContent result:
                            var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                                ? n
                                : "unknown";
                            toolResults.Add(new ToolResult(name, result.Result?.ToString() ?? ""));
                            break;
                    }
                }
            }
        }, ct);

        logger.LogInformation(
            "Agent execution complete — {ChunkCount} narrative chunks, {ToolCount} tool results",
            narrativeChunks.Count, toolResults.Count);

        foreach (var chunk in narrativeChunks)
            yield return chunk;

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
