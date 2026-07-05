using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Evals.Harness;

/// <summary>The captured result of one eval turn: the ordered tool calls and results, plus a ChatResponse that
/// packages those calls as FunctionCallContent for the evaluator.</summary>
public sealed record TurnOutcome(
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<ToolResult> ToolResults,
    ChatResponse Response,
    string Narrative);

/// <summary>
/// Runs one CharacterCreation turn through the real AgentExecutor and captures the tool calls. Mirrors
/// TurnCoordinator's per-turn steps minus the transaction/SSE layer.
/// </summary>
public sealed class EvalTurnRunner(
    AsyncServiceScope scope,
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    Guid sessionId,
    Guid chatSessionId) : IAsyncDisposable
{
    public async Task<TurnOutcome> RunTurnAsync(string playerMessage, CancellationToken ct = default)
    {
        var context = await contextLoader.LoadAsync(sessionId, ct);
        var stage = context.DeriveStage();
        var (tools, _) = toolProvider.GetToolsForStage(context, stage);

        await chatHistoryRepository.SaveMessage(chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);

        var toolCalls = new List<string>();
        var toolResults = new List<ToolResult>();
        var narrative = new System.Text.StringBuilder();

        await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
        {
            if (evt is ToolResult tr)
            {
                toolCalls.Add(tr.Function);
                toolResults.Add(tr);
            }
            else if (evt is NarrativeChunk chunk)
                narrative.Append(chunk.Text);
        }

        await chatHistoryRepository.SaveMessage(
            chatSessionId,
            new ChatMessage(ChatRole.Assistant, narrative.ToString()) { AuthorName = "Game_Master" },
            ct);

        var response = BuildToolCallResponse(toolCalls, narrative.ToString());
        return new TurnOutcome(toolCalls, toolResults, response, narrative.ToString());
    }

    private static ChatResponse BuildToolCallResponse(IReadOnlyList<string> toolCalls, string narrative)
    {
        var contents = new List<AIContent>();
        for (int i = 0; i < toolCalls.Count; i++)
            contents.Add(new FunctionCallContent($"call_{i}", toolCalls[i]));
        if (!string.IsNullOrEmpty(narrative))
            contents.Add(new TextContent(narrative));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
