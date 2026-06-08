using System.Collections.Frozen;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Framework-agnostic allow-list of which plugin functions are exposed to the model in each
/// <see cref="SessionStage"/>. This is the hard guardrail behind stage-scoped tool registration:
/// the agent for a given turn is built with ONLY these tools, so out-of-stage actions are
/// physically impossible rather than merely discouraged by the prompt.
///
/// Kept as plain data (no Semantic Kernel / Agent Framework types) so it is shared unchanged
/// across the SK→Agent Framework migration and pinned directly by tests.
///
/// Shape: Stage -> { "PluginName" -> [ "FunctionName", ... ] }
/// </summary>
public static class StageToolMap
{
    public static readonly FrozenDictionary<SessionStage, FrozenDictionary<string, FrozenSet<string>>> Map =
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
}
