#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Polly.Registry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Services;

public sealed class AgentExecutor(
    IChatHistoryRepository chatHistoryRepository,
    PromptComposer promptComposer,
    ResiliencePipelineProvider<string> resilienceProvider,
    ILogger<AgentExecutor> logger) : IAgentExecutor
{
    public async IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        Kernel kernel,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = KernelFactory.ActivitySource.StartActivity("AgentExecutor.ExecuteAsync");
        activity?.SetTag("session.chat_id", chatSessionId.ToString());

        var chatHistory = await chatHistoryRepository.LoadSession(chatSessionId, ct) ?? new ChatHistory();
        var agent = CreateAgent(kernel, sessionContext);

        var pipeline = resilienceProvider.GetPipeline("llm-retry");
        var narrativeChunks = new List<NarrativeChunk>();
        var toolResults = new List<ToolResult>();

        await pipeline.ExecuteAsync(async token =>
        {
            narrativeChunks.Clear();
            toolResults.Clear();

            ChatHistoryAgentThread thread = new(chatHistory);
            var userMessage = new ChatMessageContent(AuthorRole.User, playerMessage);

            await foreach (var response in agent.InvokeStreamingAsync(userMessage, thread, cancellationToken: token))
            {
                if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                    continue;

                var content = response.Message.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    narrativeChunks.Add(new NarrativeChunk(content));
                }
            }

            await foreach (var completed in thread.GetMessagesAsync(token))
            {
                foreach (var item in completed.Items)
                {
                    if (item is FunctionResultContent funcResult)
                    {
                        toolResults.Add(new ToolResult(
                            funcResult.FunctionName ?? "unknown",
                            funcResult.Result ?? ""));
                    }
                }
            }
        }, ct);

        logger.LogInformation(
            "Agent execution complete — {ChunkCount} narrative chunks, {ToolCount} tool results",
            narrativeChunks.Count, toolResults.Count);

        foreach (var chunk in narrativeChunks)
        {
            yield return chunk;
        }

        foreach (var toolResult in toolResults)
        {
            yield return toolResult;
        }
    }

    private ChatCompletionAgent CreateAgent(Kernel kernel, SessionContext sessionContext)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var summarizer = new ChatHistorySummarizationReducer(
            chatService,
            targetCount: 100,
            thresholdCount: 150)
        {
            SummarizationInstructions =
                """
                When summarizing this MORK BORG game session, preserve these critical elements:

                ESSENTIAL GAME STATE:
                - Character names, current hit points, abilities, scars, and omens
                - Current campaign location and time of day/season
                - Active encounters (adversaries, their status, ongoing combat)
                - Important NPCs the characters have met and their relationships
                - Key items, weapons, or artifacts in possession
                - Current goals, quests, or destinations
                - Recent significant events that affect the narrative

                PRESERVE THE ATMOSPHERE:
                - Maintain the doom-laden, apocalyptic tone of MORK BORG
                - Keep descriptions of the decaying world and mounting dread
                - Retain any omens, prophecies, or signs of the coming end
                - Preserve the dark humor and grim moments

                CONDENSE BUT KEEP:
                - Dialogue that reveals character or advances plot
                - Combat outcomes and their consequences (wounds, deaths, victories)
                - Environmental hazards or threats still present
                - Any clues, mysteries, or plot hooks still unresolved

                DISCARD:
                - Repetitive descriptions unless they build atmosphere
                - Resolved minor encounters with no lasting impact
                - Excessive back-and-forth without narrative progress
                - Redundant explanations of rules or mechanics

                Format the summary as a narrative that maintains the MORK BORG tone while clearly stating the current game state.
                """
        };

        return new ChatCompletionAgent
        {
            Name = "Game_Master",
            HistoryReducer = summarizer,
            Instructions = promptComposer.Compose(sessionContext),
            Kernel = kernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };
    }
}
