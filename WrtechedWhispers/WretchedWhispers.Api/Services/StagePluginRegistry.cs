using Microsoft.SemanticKernel;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Maps each session stage to the set of kernel functions the model is allowed to call.
/// Used with FunctionChoiceBehavior.Auto(functions: ...) to gate tool availability per stage.
/// </summary>
public sealed class StagePluginRegistry
{
    public IReadOnlyList<KernelFunction> GetFunctionsForStage(SessionStage stage, Kernel kernel)
    {
        return stage switch
        {
            SessionStage.CharacterCreation => GetFunctions(kernel,
                ("Character", ["CreateCharacter"])),

            SessionStage.CampaignSetup => GetFunctions(kernel,
                ("Campaign", ["CreateCampaign", "AddCharacterToCampaign", "StartCampaign"])),

            SessionStage.Exploration => GetFunctions(kernel,
                ("Encounter", ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"]),
                ("Campaign", ["AdvanceTime", "Rest"]),
                ("Character", ["ChallengeCharacter", "AddItemToCharacterInventory", "BuyItem", "CastScroll"]),
                ("Dice", ["Roll"])),

            SessionStage.Combat => GetFunctions(kernel,
                ("Encounter", ["AttackPlayer", "AttackAdversary", "EndEncounter"]),
                ("Dice", ["Roll"])),

            SessionStage.Resolution => GetFunctions(kernel,
                ("Character", ["AddItemToCharacterInventory", "RemoveItemFromCharacterInventory",
                    "InfectCharacter", "CureInfection", "ImproveCharacterAbility", "DegradeCharacterAbility"]),
                ("Campaign", ["AdvanceTime"]),
                ("Resolution", ["CompleteResolution"])),

            SessionStage.Ended => [],

            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown session stage")
        };
    }

    private static List<KernelFunction> GetFunctions(
        Kernel kernel, params (string Plugin, string[] Functions)[] specs)
    {
        var result = new List<KernelFunction>();
        foreach (var (plugin, functions) in specs)
        {
            foreach (var func in functions)
            {
                result.Add(kernel.Plugins.GetFunction(plugin, func));
            }
        }
        return result;
    }
}
