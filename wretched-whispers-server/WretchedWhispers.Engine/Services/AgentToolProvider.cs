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
/// Agent Framework replacement for the former KernelFactory. Exposes every game tool in
/// <see cref="GameToolCatalog"/> as an <see cref="AIFunction"/> bound to this turn's instances, and
/// wraps each in the guards that enforce its attribute: the stage it may be called in (checked against
/// the stage at the moment of the call), its per-turn call limit, and the end of the turn's actions.
/// Tool names are the bare method names, which is what the stage prompts reference.
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

    public (IReadOnlyList<AIFunction> Tools, string[] RegisteredFunctions) GetTools(SessionContext sessionContext)
    {
        using var activity = ActivitySource.StartActivity("AgentToolProvider.GetTools");
        var stage = sessionContext.DeriveStage();
        activity?.SetTag("session.stage", stage.ToString());

        // A characterless session is a broken one, and an ended story takes no actions: no tools at all.
        if (stage is SessionStage.CharacterCreation or SessionStage.Ended)
        {
            logger.LogInformation("Stage {Stage}: no tools registered", stage);
            return ([], []);
        }

        var descriptors = GameToolCatalog.All;

        // Per-turn tool instances, keyed by type so a catalog descriptor binds to the right one. They
        // are built here (not DI-registered) because each needs the turn's SessionContext. The set of
        // types must match GameToolCatalog.ToolTypes. Cross-aggregate linking (character/encounter ->
        // campaign) is delegated to CampaignService, so the tools stay thin.
        var instances = new Dictionary<Type, object>
        {
            [typeof(CharacterTools)] = new CharacterTools(
                charactersRepository, characterService, encounterService, campaignService, sessionContext),
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

            AIFunction function = AIFunctionFactory.Create(descriptor.Method, instance, new AIFunctionFactoryOptions
            {
                Name = descriptor.Name,
                Description = descriptor.Description
            });
            // A fresh wrapper per call of this method = per turn, so the count starts at zero each turn.
            if (descriptor.MaxCallsPerTurn > 0)
                function = new PerTurnCallLimit(function, descriptor.MaxCallsPerTurn);
            function = new StageGuard(function, sessionContext, descriptor.Stages);
            // Outermost: once StartEncounter has ended the turn, the live stage is Combat and the round
            // tool is in stage, so only this guard keeps the blow the player declared unstruck.
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

/// <summary>
/// Enforces a tool's <see cref="GameToolAttribute"/> stages against the stage at the moment of the call,
/// which the encounter tools keep current in the turn's <see cref="SessionContext"/>. A refused call
/// throws before reaching the domain, with a message the model can act on.
/// </summary>
internal sealed class StageGuard(AIFunction inner, SessionContext context, IReadOnlyList<SessionStage> stages)
    : DelegatingAIFunction(inner)
{
    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var stage = context.DeriveStage();
        if (!stages.Contains(stage))
            throw new InvalidOperationException(
                $"{Name} cannot be called {GameToolCatalog.Phrase(stage)}. {WhatNow(stage)}");
        return base.InvokeCoreAsync(arguments, cancellationToken);
    }

    private static string WhatNow(SessionStage stage) => stage switch
    {
        SessionStage.Combat => "A fight is on: the player's action is resolved with ResolveCombatRound, after "
            + "UseItemFromCharacterInventory or CastScroll when they use an item or a scroll.",
        SessionStage.Resolution => "The fight is over: call CompleteResolution to close its aftermath, and "
            + "the tools for outside a fight work again this same turn.",
        SessionStage.Exploration => "No fight is on, and none has just ended.",
        SessionStage.CampaignSetup => "The campaign has not begun: call ConfigureCampaign.",
        _ => "The story has ended: call no more tools."
    };
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
