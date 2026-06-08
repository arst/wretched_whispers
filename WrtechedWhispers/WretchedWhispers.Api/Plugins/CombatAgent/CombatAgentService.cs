using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Prompts;
using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Plugins.CombatAgent;

/// <summary>
/// Combat sub-agent that resolves encounters mechanically with only combat tools.
/// Domain state is mutated by tool calls during execution; must run within the same DI
/// scope/transaction as the game-master turn.
///
/// NOTE (Phase 1): this preserves the original autonomous multi-round loop verbatim so the
/// SK→Agent Framework port stays behavior-preserving. Phase 2a replaces it with player-driven
/// rounds.
/// </summary>
public sealed class CombatAgentService(
    IChatClient chatClient,
    ILogger<CombatAgentService> logger) : ICombatAgentService
{
    private const int MaxIterations = 30;

    public async IAsyncEnumerable<GameTurnEvent> ResolveCombatAsync(
        SessionContext sessionContext,
        IReadOnlyList<AIFunction> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var combatAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Combat_Resolver",
            ChatOptions = new ChatOptions
            {
                Instructions = CombatPrompts.ComposeWithContext(sessionContext),
                Tools = tools.Cast<AITool>().ToList()
            }
        });

        var session = await combatAgent.CreateSessionAsync(ct);
        var iteration = 0;

        var combatMessage =
            "Resolve this combat encounter. Attack with adversaries, let the player fight back, and end the encounter when all adversaries are dead or fled.";

        while (iteration < MaxIterations)
        {
            iteration++;
            logger.LogDebug("Combat iteration {Iteration}/{MaxIterations}", iteration, MaxIterations);

            var callNames = new Dictionary<string, string>();

            await foreach (var update in combatAgent.RunStreamingAsync(combatMessage, session, null, ct))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            yield return new NarrativeChunk(text.Text);
                            break;
                        case FunctionCallContent call when call.CallId is not null:
                            callNames[call.CallId] = call.Name;
                            break;
                        case FunctionResultContent result:
                            var name = result.CallId is not null && callNames.TryGetValue(result.CallId, out var n)
                                ? n
                                : "unknown";
                            yield return new ToolResult(name, result.Result?.ToString() ?? "");
                            break;
                    }
                }
            }

            // Check if encounter ended (domain state mutated by EndEncounter tool call)
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

            combatMessage = "Continue the combat. Attack with remaining adversaries and let the player respond.";
        }

        if (iteration >= MaxIterations)
            logger.LogWarning("Combat reached max iterations ({MaxIterations})", MaxIterations);
    }
}
