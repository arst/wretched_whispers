#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Prompts;
using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Plugins.CombatAgent;

/// <summary>
/// Combat sub-agent that resolves encounters mechanically with only combat tools.
/// Domain state is mutated by plugin calls during execution (D-03).
/// Must be called within the same DI scope/transaction as the game master turn.
/// </summary>
public sealed class CombatAgentService(ILogger<CombatAgentService> logger) : ICombatAgentService
{
    private const int MaxIterations = 30;

    public async IAsyncEnumerable<GameTurnEvent> ResolveCombatAsync(
        SessionContext sessionContext,
        Kernel gameKernel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var combatAgent = new ChatCompletionAgent
        {
            Name = "Combat_Resolver",
            Instructions = CombatPrompts.ComposeWithContext(sessionContext),
            Kernel = gameKernel,
            Arguments = new KernelArguments(
                new AzureOpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
        };

        var thread = new ChatHistoryAgentThread();
        var iteration = 0;

        // Initial combat prompt
        var combatMessage = new ChatMessageContent(AuthorRole.User,
            "Resolve this combat encounter. Attack with adversaries, let the player fight back, and end the encounter when all adversaries are dead or fled.");

        while (iteration < MaxIterations)
        {
            iteration++;
            logger.LogDebug("Combat iteration {Iteration}/{MaxIterations}", iteration, MaxIterations);

            await foreach (var response in combatAgent.InvokeStreamingAsync(
                combatMessage, thread, cancellationToken: ct))
            {
                if (response.Message.Role is not null && response.Message.Role != AuthorRole.Assistant)
                    continue;

                var content = response.Message.Content;
                if (!string.IsNullOrEmpty(content))
                {
                    yield return new NarrativeChunk(content);
                }
            }

            // Extract tool results from thread messages for this iteration
            await foreach (var completed in thread.GetMessagesAsync(ct))
            {
                foreach (var item in completed.Items)
                {
                    if (item is FunctionResultContent funcResult)
                    {
                        yield return new ToolResult(
                            funcResult.FunctionName ?? "unknown",
                            funcResult.Result ?? "");
                    }
                }
            }

            // Check if encounter ended (domain state mutated by EndEncounter plugin call)
            if (sessionContext.ActiveEncounter is not null && sessionContext.ActiveEncounter.IsEnded)
            {
                logger.LogDebug("Combat ended — encounter resolved at iteration {Iteration}", iteration);
                break;
            }

            // Check if character died
            if (sessionContext.Character is not null && sessionContext.Character.IsDead)
            {
                logger.LogDebug("Combat ended — character died at iteration {Iteration}", iteration);
                break;
            }

            // Next round prompt
            combatMessage = new ChatMessageContent(AuthorRole.User,
                "Continue the combat. Attack with remaining adversaries and let the player respond.");
        }

        if (iteration >= MaxIterations)
        {
            logger.LogWarning("Combat reached max iterations ({MaxIterations})", MaxIterations);
        }
    }
}
