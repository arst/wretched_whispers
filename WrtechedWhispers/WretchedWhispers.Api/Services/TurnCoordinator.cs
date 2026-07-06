using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Orchestrates one game-master turn: resolve the chat session, load context and derive the stage,
/// run the stage-scoped agent inside a single atomic unit of work, persist the exchange, and stream
/// the resulting events. Transaction mechanics live behind <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    ITurnTraceRepository turnTraceRepository,
    IUnitOfWork unitOfWork,
    ILogger<TurnCoordinator> logger)
{
    private static readonly JsonSerializerOptions TraceJson =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        CancellationToken ct) =>
        RunProducer(
            (writer, token) => ProduceEventsAsync(writer, sessionId, playerMessage, token),
            ct);

    private static async IAsyncEnumerable<GameTurnEvent> RunProducer(
        Func<ChannelWriter<GameTurnEvent>, CancellationToken, Task> produce,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<GameTurnEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceAndCompleteAsync(produce, channel.Writer, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    private static async Task ProduceAndCompleteAsync(
        Func<ChannelWriter<GameTurnEvent>, CancellationToken, Task> produce,
        ChannelWriter<GameTurnEvent> writer,
        CancellationToken ct)
    {
        try
        {
            await produce(writer, ct);
            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
    }

    private async Task ProduceEventsAsync(
        ChannelWriter<GameTurnEvent> writer,
        Guid sessionId,
        string playerMessage,
        CancellationToken ct)
    {
        using var activity = AgentToolProvider.ActivitySource.StartActivity("TurnCoordinator.ExecuteTurnAsync");
        activity?.SetTag("session.id", sessionId.ToString());

        // Resolve the chat session.
        var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
        var chatSessionId = chatSessions.FirstOrDefault();
        if (chatSessionId == Guid.Empty)
        {
            writer.TryWrite(new TurnError("No chat session found for this campaign"));
            return;
        }

        // Load context and derive the stage (locked for the whole turn).
        var context = await contextLoader.LoadAsync(sessionId, ct);
        if (context.Campaign is null)
        {
            writer.TryWrite(new TurnError("Session not found"));
            return;
        }

        var stage = context.DeriveStage();

        // Domain is final: a dead character or an ended world accepts no further actions. Do NOT run the
        // narrator — with no tools it cannot mutate anything, but it could still fabricate a "revival" in
        // prose (which is exactly what happened). Re-sync the client to the ended state and refuse the turn.
        if (stage == SessionStage.Ended)
        {
            logger.LogInformation("Turn refused — session already ended. Session={SessionId}", sessionId);
            writer.TryWrite(StateUpdateMapper.Map(context));
            writer.TryWrite(new TurnError("This story has ended. Begin a new character to continue."));
            return;
        }

        var (tools, registeredFunctions) = toolProvider.GetToolsForStage(context, stage);
        activity?.SetTag("session.stage", stage.ToString());
        activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

        // Snapshot the state the model sees as INPUT for this turn, frozen to a string before any tool
        // mutates the loaded aggregates. This is the "why did the model do X" context for error analysis.
        var gameStateJson = JsonSerializer.Serialize(StateUpdateMapper.Map(context), TraceJson);

        try
        {
            // One atomic unit of work for the turn. Disposal rolls back if we don't commit.
            await using var uow = await unitOfWork.BeginAsync(ct);

            await chatHistoryRepository.SaveMessage(
                chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);

            var narrativeChunks = new List<NarrativeChunk>();
            var toolResults = new List<ToolResult>();
            AgentTrace? agentTrace = null;

            // Every stage — including Combat — runs one agent turn per player message.
            await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
            {
                // AgentTrace is an out-of-band capture event — persist it, never stream it to the client.
                if (evt is AgentTrace trace)
                {
                    agentTrace = trace;
                    continue;
                }

                writer.TryWrite(evt);

                if (evt is NarrativeChunk chunk)
                    narrativeChunks.Add(chunk);
                else if (evt is ToolResult tool)
                    toolResults.Add(tool);
            }

            var fullResponse = new StringBuilder();
            foreach (var chunk in narrativeChunks)
                fullResponse.Append(chunk.Text);

            await chatHistoryRepository.SaveMessage(
                chatSessionId,
                new ChatMessage(ChatRole.Assistant, fullResponse.ToString()) { AuthorName = "Game_Master" },
                ct);

            await turnTraceRepository.Save(
                BuildTrace(sessionId, chatSessionId, stage, playerMessage, gameStateJson,
                    agentTrace, toolResults, fullResponse.ToString()),
                ct);

            await uow.CommitAsync(ct);

            // Reload post-commit so the client sees committed state.
            var postTurnContext = await contextLoader.LoadAsync(sessionId, ct);
            writer.TryWrite(StateUpdateMapper.Map(postTurnContext));
            writer.TryWrite(new TurnDone());

            logger.LogInformation(
                "Turn complete — Session={SessionId}, Stage={Stage}, NarrativeChunks={ChunkCount}, ToolResults={ToolCount}",
                sessionId, stage, narrativeChunks.Count, toolResults.Count);
        }
        catch (OperationCanceledException)
        {
            // uow disposal rolled back.
            writer.TryWrite(new TurnError("Request was cancelled"));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another turn for this same session committed while this one ran (double-submit / retry).
            // uow disposal rolled back. The SSE response is already open, so this is a turn-level error
            // rather than the pre-stream 409 the SessionConcurrencyGuard gives.
            logger.LogWarning(ex,
                "Concurrent turn conflict — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("This session was updated by another action. Please retry."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turn failed — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("An error occurred while processing your action"));
        }
    }

    // Assembles one turn trace row. Tool-call arguments and tool results are stored as embedded JSON
    // (not escaped strings) so the exporter emits clean nested objects for error analysis.
    private static TurnTraceEntity BuildTrace(
        Guid campaignId,
        Guid chatSessionId,
        SessionStage stage,
        string playerMessage,
        string gameStateJson,
        AgentTrace? agentTrace,
        IReadOnlyList<ToolResult> toolResults,
        string narrative)
    {
        var callsArray = new JsonArray();
        foreach (var call in agentTrace?.ToolCalls ?? [])
            callsArray.Add(new JsonObject
            {
                ["name"] = call.Name,
                ["arguments"] = call.Arguments is null ? null : JsonNode.Parse(call.Arguments)
            });

        var toolResultsJson = JsonSerializer.Serialize(
            toolResults.Select(t => new { name = t.Function, result = t.Result }), TraceJson);

        return new TurnTraceEntity
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            ChatSessionId = chatSessionId,
            Stage = stage.ToString(),
            Timestamp = DateTime.UtcNow,
            PlayerMessage = playerMessage,
            GameStateJson = gameStateJson,
            ToolCallsJson = callsArray.ToJsonString(),
            ToolResultsJson = toolResultsJson,
            SuppressedNarrative = agentTrace?.SuppressedNarrative,
            Narrative = narrative
        };
    }
}
