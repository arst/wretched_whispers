#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.Services;

public sealed class KernelFactory(
    IServiceProvider serviceProvider,
    IOptions<AzureOpenAiSettings> azureSettings,
    ILogger<KernelFactory> logger)
{
    internal static readonly ActivitySource ActivitySource = new("WretchedWhispers.GameTurn");

    /// <summary>
    /// Stage -> { "PluginName" -> [ "FunctionName", ... ] }
    /// </summary>
    private static readonly FrozenDictionary<SessionStage, FrozenDictionary<string, FrozenSet<string>>> StageMap =
        new Dictionary<SessionStage, FrozenDictionary<string, FrozenSet<string>>>
        {
            [SessionStage.CharacterCreation] = new Dictionary<string, FrozenSet<string>>
            {
                ["Character"] = new[] { "CreateCharacter" }.ToFrozenSet()
            }.ToFrozenDictionary(),

            [SessionStage.CampaignSetup] = new Dictionary<string, FrozenSet<string>>
            {
                ["Campaign"] = new[] { "ConfigureCampaign", "StartCampaign" }.ToFrozenSet()
            }.ToFrozenDictionary(),

            [SessionStage.Exploration] = new Dictionary<string, FrozenSet<string>>
            {
                ["Character"] = new[] { "ChallengeCharacter", "AddItemToCharacterInventory", "BuyItem", "CastScroll" }.ToFrozenSet(),
                ["Campaign"] = new[] { "AdvanceTime", "Rest" }.ToFrozenSet(),
                ["Encounter"] = new[] { "CreateEncounter", "AddAdversaryToEncounter", "StartEncounter" }.ToFrozenSet(),
                ["Dice"] = new[] { "Roll" }.ToFrozenSet()
            }.ToFrozenDictionary(),

            [SessionStage.Combat] = new Dictionary<string, FrozenSet<string>>
            {
                ["Encounter"] = new[] { "AttackPlayer", "AttackAdversary", "EndEncounter" }.ToFrozenSet(),
                ["Dice"] = new[] { "Roll" }.ToFrozenSet()
            }.ToFrozenDictionary(),

            [SessionStage.Resolution] = new Dictionary<string, FrozenSet<string>>
            {
                ["Character"] = new[] { "AddItemToCharacterInventory", "RemoveItemFromCharacterInventory", "InfectCharacter", "CureInfection", "ImproveCharacterAbility", "DegradeCharacterAbility" }.ToFrozenSet(),
                ["Campaign"] = new[] { "AdvanceTime", "Rest" }.ToFrozenSet(),
                ["Resolution"] = new[] { "CompleteResolution" }.ToFrozenSet()
            }.ToFrozenDictionary(),

            [SessionStage.Ended] = new Dictionary<string, FrozenSet<string>>().ToFrozenDictionary()
        }.ToFrozenDictionary();

    public (Kernel Kernel, string[] RegisteredFunctions) CreateForStage(SessionContext sessionContext, SessionStage stage)
    {
        using var activity = ActivitySource.StartActivity("KernelFactory.CreateForStage");
        activity?.SetTag("session.stage", stage.ToString());

        var settings = azureSettings.Value;
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.AddAzureOpenAIChatCompletion(
            settings.ChatModelDeployment,
            settings.Endpoint,
            settings.ApiKey);

        var kernel = kernelBuilder.Build();

        if (!StageMap.TryGetValue(stage, out var allowedPlugins) || allowedPlugins.Count == 0)
        {
            logger.LogInformation("Stage {Stage}: no functions registered (kernel is empty)", stage);
            return (kernel, []);
        }

        // Build all wrapper plugins (same as GameSessionService.BuildKernelForSession)
        var charOps = serviceProvider.GetRequiredService<ICharacterOperations>();
        var campaignOps = serviceProvider.GetRequiredService<ICampaignOperations>();
        var encounterOps = serviceProvider.GetRequiredService<IEncounterOperations>();
        var diceOps = serviceProvider.GetRequiredService<IDiceOperations>();
        var campaignsRepo = serviceProvider.GetRequiredService<ICampaignsRepository>();
        var encountersRepo = serviceProvider.GetRequiredService<IEncountersRepository>();

        var wrappers = new Dictionary<string, object>
        {
            ["Character"] = new CharacterWrapperPlugin(charOps, sessionContext, campaignsRepo),
            ["Campaign"] = new CampaignWrapperPlugin(campaignOps, campaignsRepo, sessionContext),
            ["Encounter"] = new EncounterWrapperPlugin(encounterOps, sessionContext),
            ["Dice"] = new DiceWrapperPlugin(diceOps),
            ["Resolution"] = new ResolutionWrapperPlugin(sessionContext, encountersRepo)
        };

        var registeredFunctions = new List<string>();

        foreach (var (pluginName, allowedFunctionNames) in allowedPlugins)
        {
            if (!wrappers.TryGetValue(pluginName, out var wrapper))
                continue;

            // Import the full plugin temporarily to get all KernelFunctions
            var tempPlugin = kernel.ImportPluginFromObject(wrapper, pluginName);

            // Filter to only allowed functions
            var filteredFunctions = tempPlugin
                .Where(f => allowedFunctionNames.Contains(f.Name))
                .ToList();

            // Remove the temp plugin
            kernel.Plugins.Remove(tempPlugin);

            // Re-add with only the filtered functions
            var scopedPlugin = KernelPluginFactory.CreateFromFunctions(pluginName, filteredFunctions);
            kernel.Plugins.Add(scopedPlugin);

            foreach (var fn in filteredFunctions)
            {
                registeredFunctions.Add($"{pluginName}.{fn.Name}");
            }
        }

        var result = registeredFunctions.ToArray();
        logger.LogInformation("Stage {Stage}: registered {Count} functions — [{Functions}]",
            stage, result.Length, string.Join(", ", result));

        return (kernel, result);
    }
}
