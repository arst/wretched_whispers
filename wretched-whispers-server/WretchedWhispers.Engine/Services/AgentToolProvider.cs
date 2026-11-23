using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Agent Framework replacement for the former KernelFactory. Constructs the stage-scoped game-tool
/// classes and exposes their allowed methods as <see cref="AIFunction"/> tools. Which tools a stage
/// exposes — and the bound <see cref="System.Reflection.MethodInfo"/> for each — comes from
/// <see cref="GameToolCatalog"/> (derived once from <see cref="GameToolAttribute"/>); this class only
/// supplies the per-turn instances. Tool names are the bare method names, which is what the stage
/// prompts reference.
/// </summary>
public sealed class AgentToolProvider(
    ICharactersRepository charactersRepository,
    IEncountersRepository encountersRepository,
    CharacterService characterService,
    CampaignService campaignService,
    EncounterService encounterService,
    Dice dice,
    ILogger<AgentToolProvider> logger) : IAgentToolProvider
{
    /// <summary>Shared trace source for the whole game-turn pipeline.</summary>
    internal static readonly ActivitySource ActivitySource = new("WretchedWhispers.GameTurn");

    public (IReadOnlyList<AIFunction> Tools, string[] RegisteredFunctions) GetToolsForStage(
        SessionContext sessionContext, SessionStage stage)
    {
        using var activity = ActivitySource.StartActivity("AgentToolProvider.GetToolsForStage");
        activity?.SetTag("session.stage", stage.ToString());

        var descriptors = GameToolCatalog.ForStage(stage);
        if (descriptors.Count == 0)
        {
            logger.LogInformation("Stage {Stage}: no tools registered", stage);
            return ([], []);
        }

        // Per-turn tool instances, keyed by type so a catalog descriptor binds to the right one. They
        // are built here (not DI-registered) because each needs the turn's SessionContext. The set of
        // types must match GameToolCatalog.ToolTypes. Cross-aggregate linking (character/encounter ->
        // campaign) is delegated to CampaignService, so the tools stay thin.
        var instances = new Dictionary<Type, object>
        {
            [typeof(CharacterTools)] = new CharacterTools(
                charactersRepository, characterService, encounterService, sessionContext),
            [typeof(CampaignTools)] = new CampaignTools(
                campaignService, sessionContext),
            [typeof(EncounterTools)] = new EncounterTools(
                encounterService, encountersRepository, campaignService, sessionContext),
            [typeof(DiceTools)] = new DiceTools(dice)
        };

        var tools = new List<AIFunction>(descriptors.Count);
        var registeredFunctions = new List<string>(descriptors.Count);
        // One per call of this method = per turn, shared by all of the turn's tools.
        var turnEnd = new TurnEndGate();

        foreach (var descriptor in descriptors)
        {
            if (!instances.TryGetValue(descriptor.DeclaringType, out var instance))
            {
                logger.LogWarning(
                    "No per-turn instance constructed for tool type {Type}; tool {Tool} skipped",
                    descriptor.DeclaringType.Name, descriptor.Name);
                continue;
            }

            var function = AIFunctionFactory.Create(descriptor.Method, instance, new AIFunctionFactoryOptions
            {
                Name = descriptor.Name
            });
            // A fresh wrapper per call of this method = per turn, so the count starts at zero each turn.
            if (descriptor.MaxCallsPerTurn > 0)
                function = new PerTurnCallLimit(function, descriptor.MaxCallsPerTurn);
            tools.Add(new TurnEndGuard(function, turnEnd, descriptor.EndsTurn, descriptor.AllowedAfterTurnEnd));
            registeredFunctions.Add($"{descriptor.Group}.{descriptor.Name}");
        }

        var result = registeredFunctions.ToArray();
        logger.LogInformation("Stage {Stage}: registered {Count} tools — [{Functions}]",
            stage, result.Length, string.Join(", ", result));

        return (tools, result);
    }
}

/// <summary>
/// Enforces a tool's <see cref="GameToolAttribute.MaxCallsPerTurn"/>. Only calls that complete count,
/// so a call rejected by validation (a bad target name, say) can be retried with corrected arguments.
/// Once the limit is reached, further calls throw before reaching the domain; the function-invocation
/// loop hands the message back to the model (IncludeDetailedErrors).
/// </summary>
internal sealed class PerTurnCallLimit(AIFunction inner, int maxCalls) : DelegatingAIFunction(inner)
{
    private int _completed;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _completed) >= maxCalls)
            throw new InvalidOperationException(
                $"{Name} was already called this turn; narrate its result and stop.");

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        Interlocked.Increment(ref _completed);
        return result;
    }
}

/// <summary>Per-turn record of the tool (if any) that ended the turn's game actions.</summary>
internal sealed class TurnEndGate
{
    private volatile string? _endedBy;
    public string? EndedBy => _endedBy;
    public void End(string toolName) => _endedBy ??= toolName;
}

/// <summary>
/// Enforces <see cref="GameToolAttribute.EndsTurn"/>. Once a turn-ending tool has COMPLETED (a
/// rejected call ends nothing), every other tool call that turn throws before reaching the domain,
/// with a message the model can act on — except record-keeping tools. Without this, a model that has
/// just started combat reaches for whatever is still on offer (an ability test, a bare dice roll) to
/// "resolve" the attack the player declared, with the wrong mechanic.
/// </summary>
internal sealed class TurnEndGuard(AIFunction inner, TurnEndGate gate, bool endsTurn, bool allowedAfterEnd)
    : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (gate.EndedBy is { } endedBy && !allowedAfterEnd)
            throw new InvalidOperationException(
                $"{endedBy} ended this turn's actions, so {Name} cannot be called now. Call no more tools: "
                + "narrate what has just begun and stop. The player's next message is their first action in it.");

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);
        if (endsTurn) gate.End(Name);
        return result;
    }
}
