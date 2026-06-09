using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Orchestrates one game-master turn: resolve the chat session, load context and derive the stage,
/// run the stage-scoped agent inside a single atomic unit of work, persist the exchange, and stream
/// the resulting events. Transaction mechanics live behind <see cref="IUnitOfWork"/> and the
/// channel/yield plumbing behind <see cref="AsyncStreamBridge"/> — this class is just the sequence.
/// </summary>
public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    IUnitOfWork unitOfWork,
    ILogger<TurnCoordinator> logger)
{
    public IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        CancellationToken ct) =>
        AsyncStreamBridge.Run<GameTurnEvent>(
            (writer, token) => ProduceEventsAsync(writer, sessionId, playerMessage, token),
            ct);

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
        var (tools, registeredFunctions) = toolProvider.GetToolsForStage(context, stage);
        activity?.SetTag("session.stage", stage.ToString());
        activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

        try
        {
            // One atomic unit of work for the turn. Disposal rolls back if we don't commit.
            await using var uow = await unitOfWork.BeginAsync(ct);

            await chatHistoryRepository.SaveMessage(
                chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);

            var narrativeChunks = new List<NarrativeChunk>();
            var toolResults = new List<ToolResult>();

            // Every stage — including Combat — runs one agent turn per player message.
            await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
            {
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
}
