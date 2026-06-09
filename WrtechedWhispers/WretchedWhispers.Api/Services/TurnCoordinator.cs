using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Services;

public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    WretchedWhispersDbContext dbContext,
    ILogger<TurnCoordinator> logger)
{
    public async IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = AgentToolProvider.ActivitySource.StartActivity("TurnCoordinator.ExecuteTurnAsync");
        activity?.SetTag("session.id", sessionId.ToString());

        // Validate session — get chat session ID
        var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
        var chatSessionId = chatSessions.FirstOrDefault();
        if (chatSessionId == Guid.Empty)
        {
            yield return new TurnError("No chat session found for this campaign");
            yield break;
        }

        // Load context and derive stage
        var context = await contextLoader.LoadAsync(sessionId, ct);
        if (context.Campaign is null)
        {
            yield return new TurnError("Session not found");
            yield break;
        }

        var stage = context.DeriveStage();
        var (tools, registeredFunctions) = toolProvider.GetToolsForStage(context, stage);

        activity?.SetTag("session.stage", stage.ToString());
        activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

        // Use a channel to bridge events across the try-catch boundary
        // (C# does not allow yield inside try-catch)
        var channel = Channel.CreateUnbounded<GameTurnEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceEventsAsync(channel.Writer, sessionId, chatSessionId, context, stage, tools, playerMessage, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    private async Task ProduceEventsAsync(
        ChannelWriter<GameTurnEvent> writer,
        Guid sessionId,
        Guid chatSessionId,
        SessionContext context,
        SessionStage stage,
        IReadOnlyList<AIFunction> tools,
        string playerMessage,
        CancellationToken ct)
    {
        try
        {
            // Begin transaction
            await dbContext.Database.BeginTransactionAsync(ct);

            try
            {
                // Save user message
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessage(ChatRole.User, playerMessage),
                    ct);

                var narrativeChunks = new List<NarrativeChunk>();
                var toolResults = new List<ToolResult>();

                // Every stage — including Combat — runs one agent turn per player message.
                // Combat is player-driven: one player message resolves exactly one round (the stage
                // stays Combat across turns via DeriveStage until EndEncounter or character death).
                await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
                {
                    writer.TryWrite(evt);

                    if (evt is NarrativeChunk chunk)
                        narrativeChunks.Add(chunk);
                    else if (evt is ToolResult tool)
                        toolResults.Add(tool);
                }

                // Build full response text from narrative chunks
                var fullResponse = new StringBuilder();
                foreach (var chunk in narrativeChunks)
                {
                    fullResponse.Append(chunk.Text);
                }

                // Save assistant response
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessage(ChatRole.Assistant, fullResponse.ToString())
                    {
                        AuthorName = "Game_Master"
                    },
                    ct);

                // Commit transaction
                await dbContext.Database.CommitTransactionAsync(ct);

                // Reload context post-turn and yield state update
                var postTurnContext = await contextLoader.LoadAsync(sessionId, ct);
                writer.TryWrite(StateUpdateMapper.Map(postTurnContext));

                // Signal turn complete
                writer.TryWrite(new TurnDone());

                logger.LogInformation(
                    "Turn complete — Session={SessionId}, Stage={Stage}, NarrativeChunks={ChunkCount}, ToolResults={ToolCount}",
                    sessionId, stage, narrativeChunks.Count, toolResults.Count);
            }
            catch (OperationCanceledException)
            {
                await RollbackSafelyAsync();
                writer.TryWrite(new TurnError("Request was cancelled"));
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Another turn for this same session committed while this one was running (two
                // overlapping turns — typically a double-submit or client retry, since the long SSE
                // turn widens the race window). The SSE response is already open, so this surfaces as
                // a turn-level error rather than the pre-stream 409 the SessionConcurrencyGuard gives.
                logger.LogWarning(ex,
                    "Concurrent turn conflict — Session={SessionId}, Stage={Stage}",
                    sessionId, stage);
                await RollbackSafelyAsync();
                writer.TryWrite(new TurnError("This session was updated by another action. Please retry."));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Turn failed — Session={SessionId}, Stage={Stage}",
                    sessionId, stage);
                await RollbackSafelyAsync();
                writer.TryWrite(new TurnError("An error occurred while processing your action"));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Turn failed before transaction — Session={SessionId}",
                sessionId);
            writer.TryWrite(new TurnError("An error occurred while processing your action"));
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task RollbackSafelyAsync()
    {
        try
        {
            await dbContext.Database.RollbackTransactionAsync();
        }
        catch
        {
            // Rollback may fail if connection is already closed
        }
    }
}
