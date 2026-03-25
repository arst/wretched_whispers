#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.CombatAgent;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Services;

public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IKernelFactory kernelFactory,
    IAgentExecutor agentExecutor,
    ICombatAgentService combatAgentService,
    IChatHistoryRepository chatHistoryRepository,
    WretchedWhispersDbContext dbContext,
    ILogger<TurnCoordinator> logger)
{
    public async IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = KernelFactory.ActivitySource.StartActivity("TurnCoordinator.ExecuteTurnAsync");
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
        var (kernel, registeredFunctions) = kernelFactory.CreateForStage(context, stage);

        activity?.SetTag("session.stage", stage.ToString());
        activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

        // Use a channel to bridge events across the try-catch boundary
        // (C# does not allow yield inside try-catch)
        var channel = Channel.CreateUnbounded<GameTurnEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceEventsAsync(channel.Writer, sessionId, chatSessionId, context, stage, kernel, playerMessage, ct);

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
        Kernel kernel,
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
                    new ChatMessageContent(AuthorRole.User, playerMessage),
                    ct);

                var narrativeChunks = new List<NarrativeChunk>();
                var toolResults = new List<ToolResult>();

                if (stage == SessionStage.Combat)
                {
                    await foreach (var evt in combatAgentService.ResolveCombatAsync(context, kernel, ct))
                    {
                        writer.TryWrite(evt);

                        if (evt is NarrativeChunk chunk)
                            narrativeChunks.Add(chunk);
                        else if (evt is ToolResult tool)
                            toolResults.Add(tool);
                    }
                }
                else
                {
                    await foreach (var evt in agentExecutor.ExecuteAsync(kernel, context, chatSessionId, playerMessage, ct))
                    {
                        writer.TryWrite(evt);

                        if (evt is NarrativeChunk chunk)
                            narrativeChunks.Add(chunk);
                        else if (evt is ToolResult tool)
                            toolResults.Add(tool);
                    }
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
                    new ChatMessageContent(AuthorRole.Assistant, fullResponse.ToString())
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
