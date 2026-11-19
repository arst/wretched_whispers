using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// The captured result of one eval turn.
/// <list type="bullet">
/// <item><see cref="Calls"/>: the tool calls the model made, in order, with their JSON arguments — read
/// from the turn trace the coordinator persisted, whether or not the turn committed.</item>
/// <item><see cref="ToolResults"/>: the tool-result events the turn emitted.</item>
/// <item><see cref="Narrative"/>: everything the player was shown, in order.
/// <see cref="NarrativeBeforeTools"/> is the part written before the first tool result arrived (empty
/// when no tool ran) — prose that cannot know any outcome yet.</item>
/// <item><see cref="Delta"/>: the committed per-turn delta (null before a character exists, or on failure).</item>
/// <item><see cref="Error"/>: the <see cref="TurnError"/> message when the coordinator failed or refused the turn.</item>
/// <item><see cref="Response"/>: <see cref="Calls"/> + narrative packaged as a ChatResponse for the evaluators.</item>
/// </list>
/// </summary>
public sealed record TurnOutcome(
    IReadOnlyList<ToolCallTrace> Calls,
    IReadOnlyList<ToolResult> ToolResults,
    ChatResponse Response,
    string Narrative,
    string NarrativeBeforeTools,
    TurnDelta? Delta,
    string? Error)
{
    /// <summary>The names of <see cref="Calls"/>, in call order.</summary>
    public IReadOnlyList<string> ToolCalls { get; } = Calls.Select(c => c.Name).ToList();
}

/// <summary>
/// Runs one player turn through the production <see cref="TurnCoordinator"/> — unit of work, session
/// lock, Ended-stage refusal, empty-response handling and chat-history persistence included — and
/// captures what it emitted. The runner adds nothing of its own to the chat history: the coordinator
/// owns that, exactly as in production.
/// </summary>
public sealed class EvalTurnRunner(
    AsyncServiceScope scope,
    TurnCoordinator coordinator,
    IServiceScopeFactory scopeFactory,
    Guid sessionId) : IAsyncDisposable
{
    public async Task<TurnOutcome> RunTurnAsync(string playerMessage, CancellationToken ct = default)
    {
        var turnId = Guid.NewGuid();
        var toolResults = new List<ToolResult>();
        var narrative = new System.Text.StringBuilder();
        string? beforeTools = null;
        TurnDelta? delta = null;
        string? error = null;

        await foreach (var evt in coordinator.ExecuteTurnAsync(sessionId, playerMessage, turnId, ct))
        {
            switch (evt)
            {
                case NarrativeChunk chunk:
                    narrative.Append(chunk.Text);
                    break;
                case ToolResult result:
                    // Events arrive in the order things happened, so this is the prose/outcome boundary.
                    beforeTools ??= narrative.ToString();
                    toolResults.Add(result);
                    break;
                case TurnDelta d:
                    delta = d;
                    break;
                case TurnError e:
                    error = e.Message;
                    break;
            }
        }

        var calls = await ReadCallsFromTraceAsync(turnId, ct);

        var text = narrative.ToString();
        return new TurnOutcome(
            calls, toolResults, BuildToolCallResponse(calls, text), text, beforeTools ?? "", delta, error);
    }

    // The coordinator traces every turn that got as far as deriving its stage, committed or not, keyed
    // by the turn id we passed. No trace (e.g. no chat session) means no calls.
    private async Task<IReadOnlyList<ToolCallTrace>> ReadCallsFromTraceAsync(Guid turnId, CancellationToken ct)
    {
        await using var queryScope = scopeFactory.CreateAsyncScope();
        var db = queryScope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
        var trace = await db.TurnTraces.AsNoTracking().SingleOrDefaultAsync(t => t.TurnId == turnId, ct);
        return trace is null ? [] : ParseCalls(trace.ToolCallsJson);
    }

    // ToolCallsJson is the coordinator's [{ name, arguments }] array, arguments embedded as a JSON object.
    private static List<ToolCallTrace> ParseCalls(string toolCallsJson) =>
        (JsonNode.Parse(toolCallsJson)?.AsArray() ?? [])
            .Select(node => new ToolCallTrace(
                node!["name"]!.GetValue<string>(),
                node["arguments"]?.ToJsonString()))
            .ToList();

    private static ChatResponse BuildToolCallResponse(IReadOnlyList<ToolCallTrace> calls, string narrative)
    {
        var contents = new List<AIContent>();
        for (int i = 0; i < calls.Count; i++)
            contents.Add(new FunctionCallContent($"call_{i}", calls[i].Name, ParseArguments(calls[i].Arguments)));
        if (!string.IsNullOrEmpty(narrative))
            contents.Add(new TextContent(narrative));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    // Values come back as JsonElement — the same shape the model client hands FunctionCallContent.
    private static Dictionary<string, object?>? ParseArguments(string? arguments) =>
        arguments is null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments);

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
