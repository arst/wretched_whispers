using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Pins the stage -> tool allow-list derived by <see cref="GameToolCatalog"/> from the
/// [GameTool] attributes. This is the hard guardrail's regression oracle: the expected sets below
/// are written out explicitly (NOT re-derived), so an accidental attribute change that exposes a
/// tool in the wrong stage fails here. Group labels come from the owning *Tools class name.
/// </summary>
public class GameToolCatalogTests
{
    private static string[] Flatten(SessionStage stage) =>
        GameToolCatalog.ForStage(stage)
            .Select(d => $"{d.Group}.{d.Name}")
            .OrderBy(x => x)
            .ToArray();

    [Fact]
    public void CharacterCreation_ExposesCreateCharacterAndCampaignSetupTools()
    {
        // The opening stage runs the whole intro in one turn (create character + configure the
        // campaign), so it also exposes the campaign-setup tool. The campaign auto-starts once
        // configured and joined -- there is no separate start tool for the model to call.
        Assert.Equal(
            new[] { "Campaign.ConfigureCampaign", "Character.CreateCharacter" },
            Flatten(SessionStage.CharacterCreation));
    }

    [Fact]
    public void CampaignSetup_ExposesConfigure()
    {
        Assert.Equal(
            new[] { "Campaign.ConfigureCampaign" },
            Flatten(SessionStage.CampaignSetup));
    }

    [Fact]
    public void Exploration_ExposesExactlySixteenTools()
    {
        Assert.Equal(
            new[]
            {
                "Campaign.AdvanceTime",
                "Campaign.RecordJournalEntry",
                "Campaign.RecordPointOfInterest",
                "Campaign.Rest",
                "Campaign.SetPartyLocation",
                "Character.AddItemToCharacterInventory",
                "Character.BuyItem",
                "Character.CastScroll",
                "Character.ChallengeCharacter",
                "Character.GettingBetter",
                "Character.UseItemFromCharacterInventory",
                "Dice.Roll",
                "Encounter.AddAdversaryToEncounter",
                "Encounter.CreateEncounter",
                "Encounter.StartEncounter",
                "Encounter.TurnEncounterHostile"
            },
            Flatten(SessionStage.Exploration));
    }

    [Fact]
    public void Combat_ExposesOnlyCombatTools()
    {
        Assert.Equal(
            new[]
            {
                "Campaign.RecordJournalEntry",
                "Character.CastScroll",
                "Character.UseItemFromCharacterInventory",
                "Dice.Roll",
                "Encounter.ResolveCombatRound"
            },
            Flatten(SessionStage.Combat));
    }

    [Fact]
    public void Resolution_ExposesResolutionToolsAndNoCreation()
    {
        Assert.Equal(
            new[]
            {
                "Campaign.AdvanceTime",
                "Campaign.RecordJournalEntry",
                "Campaign.RecordPointOfInterest",
                "Campaign.Rest",
                "Campaign.SetPartyLocation",
                "Character.AddItemToCharacterInventory",
                "Character.CureInfection",
                "Character.DegradeCharacterAbility",
                "Character.GettingBetter",
                "Character.ImproveCharacterAbility",
                "Character.InfectCharacter",
                "Character.UseItemFromCharacterInventory",
                // CompleteResolution lives on EncounterTools, so its group is "Encounter".
                "Encounter.CompleteResolution"
            },
            Flatten(SessionStage.Resolution));
    }

    [Fact]
    public void Ended_ExposesNoTools()
    {
        Assert.Empty(Flatten(SessionStage.Ended));
    }
}
