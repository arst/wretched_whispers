using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.GameTools;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Agent Framework replacement for the former KernelFactory. Instead of building a Semantic Kernel
/// and importing/removing plugins, it constructs the stage-scoped game-tool classes and exposes their
/// allowed methods as <see cref="AIFunction"/> tools. The stage allow-list lives in
/// <see cref="StageToolMap"/>; tool names are the bare method names (unique across tool classes), which
/// is what the stage prompts reference.
/// </summary>
public sealed class AgentToolProvider(
    IServiceProvider serviceProvider,
    ILogger<AgentToolProvider> logger) : IAgentToolProvider
{
    /// <summary>Shared trace source for the whole game-turn pipeline.</summary>
    internal static readonly ActivitySource ActivitySource = new("WretchedWhispers.GameTurn");

    public (IReadOnlyList<AIFunction> Tools, string[] RegisteredFunctions) GetToolsForStage(
        SessionContext sessionContext, SessionStage stage)
    {
        using var activity = ActivitySource.StartActivity("AgentToolProvider.GetToolsForStage");
        activity?.SetTag("session.stage", stage.ToString());

        if (!StageToolMap.Map.TryGetValue(stage, out var allowedPlugins) || allowedPlugins.Count == 0)
        {
            logger.LogInformation("Stage {Stage}: no tools registered", stage);
            return ([], []);
        }

        var campaignsRepo = serviceProvider.GetRequiredService<ICampaignsRepository>();
        var encountersRepo = serviceProvider.GetRequiredService<IEncountersRepository>();
        var charactersRepo = serviceProvider.GetRequiredService<ICharactersRepository>();
        var dice = serviceProvider.GetRequiredService<Dice>();

        // Game-tool classes auto-fill GUIDs from SessionContext, validate model arguments, and call
        // the domain directly. Constructed per turn (not DI-registered) because each needs the
        // turn's SessionContext. EncounterTools also owns the Resolution-stage CompleteResolution
        // tool, so it is registered under both the "Encounter" and "Resolution" allow-list keys.
        var encounterTools = new EncounterTools(
            serviceProvider.GetRequiredService<EncounterService>(),
            encountersRepo, dice, sessionContext, campaignsRepo);

        var wrappers = new Dictionary<string, object>
        {
            ["Character"] = new CharacterTools(
                charactersRepo,
                serviceProvider.GetRequiredService<CharacterCreationService>(),
                serviceProvider.GetRequiredService<CharacterService>(),
                dice, sessionContext, campaignsRepo),
            ["Campaign"] = new CampaignTools(
                campaignsRepo, serviceProvider.GetRequiredService<CampaignService>(), sessionContext),
            ["Encounter"] = encounterTools,
            ["Dice"] = new DiceTools(dice),
            ["Resolution"] = encounterTools
        };

        var tools = new List<AIFunction>();
        var registeredFunctions = new List<string>();

        foreach (var (pluginName, allowedFunctionNames) in allowedPlugins)
        {
            if (!wrappers.TryGetValue(pluginName, out var wrapper))
                continue;

            foreach (var functionName in allowedFunctionNames)
            {
                var method = wrapper.GetType().GetMethod(
                    functionName, BindingFlags.Public | BindingFlags.Instance);
                if (method is null)
                {
                    logger.LogWarning("Wrapper {Plugin} has no method {Function}", pluginName, functionName);
                    continue;
                }

                tools.Add(AIFunctionFactory.Create(method, wrapper, new AIFunctionFactoryOptions
                {
                    Name = functionName
                }));
                registeredFunctions.Add($"{pluginName}.{functionName}");
            }
        }

        var result = registeredFunctions.ToArray();
        logger.LogInformation("Stage {Stage}: registered {Count} tools — [{Functions}]",
            stage, result.Length, string.Join(", ", result));

        return (tools, result);
    }
}
