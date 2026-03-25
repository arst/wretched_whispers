using System.Text;
using System.Threading.Channels;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Prompts;
using WretchedWhispers.Api.Services;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

namespace WretchedWhispers.Api.Plugins.CombatAgent;

/// <summary>
/// Combat sub-agent that resolves encounters mechanically with only combat tools.
/// Domain state is mutated by plugin calls during execution (D-03).
/// Must be called within the same DI scope/transaction as the game master turn.
/// </summary>
public sealed class CombatAgentService
{
    private const int MaxIterations = 30;

    public async Task<string> ResolveCombat(
        SessionContext sessionContext,
        Kernel gameKernel,
        StagePluginRegistry stagePluginRegistry,
        ChannelWriter<SseEvent> writer,
        CancellationToken ct)
    {
        // Get combat-specific functions from the same kernel (same DI scope, same services)
        var combatFunctions = stagePluginRegistry
            .GetFunctionsForStage(SessionStage.Combat, gameKernel);

        var combatAgent = new ChatCompletionAgent
        {
            Name = "Combat_Resolver",
            Instructions = CombatPrompts.ComposeWithContext(sessionContext),
            Kernel = gameKernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        functions: combatFunctions)
                })
        };

        var combatNarrative = new StringBuilder();
        var thread = new ChatHistoryAgentThread();
        var iteration = 0;

        // Initial combat prompt
        var combatMessage = new ChatMessageContent(AuthorRole.User,
            "Resolve this combat encounter. Attack with adversaries, let the player fight back, and end the encounter when all adversaries are dead or fled.");

        while (iteration < MaxIterations)
        {
            iteration++;
            var turnText = new StringBuilder();

            await foreach (var response in combatAgent.InvokeStreamingAsync(
                combatMessage, thread, cancellationToken: ct))
            {
                if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                    continue;

                var content = response.Message.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    turnText.Append(content);
                    writer.TryWrite(new SseEvent("narrative", new { text = content }));
                }
            }

            combatNarrative.Append(turnText);

            // Extract tool results from thread messages for this iteration
            await foreach (var completed in thread.GetMessagesAsync(ct))
            {
                foreach (var item in completed.Items)
                {
                    if (item is FunctionResultContent funcResult)
                    {
                        writer.TryWrite(new SseEvent("tool_result", new
                        {
                            function = funcResult.FunctionName,
                            result = funcResult.Result
                        }));
                    }
                }
            }

            // Check if encounter ended (domain state mutated by EndEncounter plugin call)
            if (sessionContext.ActiveEncounter is not null && sessionContext.ActiveEncounter.IsEnded)
                break;

            // Check if character died
            if (sessionContext.Character is not null && sessionContext.Character.IsDead)
                break;

            // Next round prompt
            combatMessage = new ChatMessageContent(AuthorRole.User,
                "Continue the combat. Attack with remaining adversaries and let the player respond.");
        }

        return combatNarrative.ToString();
    }
}
