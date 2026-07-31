using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Engine.GameTools;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

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
                charactersRepository, characterService, dice, sessionContext),
            [typeof(CampaignTools)] = new CampaignTools(
                campaignService, sessionContext),
            [typeof(EncounterTools)] = new EncounterTools(
                encounterService, encountersRepository, campaignService, sessionContext),
            [typeof(DiceTools)] = new DiceTools(dice)
        };

        var tools = new List<AIFunction>(descriptors.Count);
        var registeredFunctions = new List<string>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (!instances.TryGetValue(descriptor.DeclaringType, out var instance))
            {
                logger.LogWarning(
                    "No per-turn instance constructed for tool type {Type}; tool {Tool} skipped",
                    descriptor.DeclaringType.Name, descriptor.Name);
                continue;
            }

            tools.Add(AIFunctionFactory.Create(descriptor.Method, instance, new AIFunctionFactoryOptions
            {
                Name = descriptor.Name
            }));
            registeredFunctions.Add($"{descriptor.Group}.{descriptor.Name}");
        }

        var result = registeredFunctions.ToArray();
        logger.LogInformation("Stage {Stage}: registered {Count} tools — [{Functions}]",
            stage, result.Length, string.Join(", ", result));

        return (tools, result);
    }
}
