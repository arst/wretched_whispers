using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Api.GameTools;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Agent Framework replacement for the former KernelFactory. Instead of building a Semantic Kernel
/// and importing/removing plugins, it builds the stage-scoped wrapper plugins and exposes their
/// allowed methods as <see cref="AIFunction"/> tools. The stage allow-list lives in
/// <see cref="StageToolMap"/>; tool names are the bare method names (unique across wrappers), which
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

        // Wrapper plugins auto-fill GUIDs from SessionContext and add guardrails over the raw
        // Semantic plugins (resolved from DI, bridged via adapters).
        var wrappers = new Dictionary<string, object>
        {
            ["Character"] = new CharacterWrapperPlugin(
                new CharacterPluginAdapter(serviceProvider.GetRequiredService<CharacterPlugin>()),
                sessionContext, campaignsRepo),
            ["Campaign"] = new CampaignWrapperPlugin(
                new CampaignPluginAdapter(serviceProvider.GetRequiredService<CampaignPlugin>()),
                campaignsRepo, sessionContext),
            ["Encounter"] = new EncounterWrapperPlugin(
                new EncounterPluginAdapter(serviceProvider.GetRequiredService<EncounterPlugin>()),
                sessionContext, campaignsRepo),
            ["Dice"] = new DiceWrapperPlugin(
                new DicePluginAdapter(serviceProvider.GetRequiredService<DicePlugin>())),
            ["Resolution"] = new ResolutionWrapperPlugin(sessionContext, encountersRepo)
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
