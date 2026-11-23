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
    public void PerTurnCallLimits_ArePinned()
    {
        // One combat action = one round; one ritual, one resolution, one encounter start per turn.
        var limited = new[] { SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution }
            .SelectMany(GameToolCatalog.ForStage)
            .Where(d => d.MaxCallsPerTurn > 0)
            .Select(d => $"{d.Group}.{d.Name}={d.MaxCallsPerTurn}")
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Character.GettingBetter=1",
                "Encounter.CompleteResolution=1",
                "Encounter.ResolveCombatRound=1",
                "Encounter.StartEncounter=1"
            },
            limited);
    }

    [Fact]
    public void TurnEndingAndRecordKeepingTools_ArePinned()
    {
        var all = new[] { SessionStage.Exploration, SessionStage.Combat, SessionStage.Resolution }
            .SelectMany(GameToolCatalog.ForStage).ToList();
        string[] Names(Func<GameToolDescriptor, bool> where) =>
            all.Where(where).Select(d => $"{d.Group}.{d.Name}").Distinct().OrderBy(x => x).ToArray();

        // Starting combat ends the turn: its first round belongs to the player's next message.
        Assert.Equal(new[] { "Encounter.StartEncounter" }, Names(d => d.EndsTurn));
        // Only notes may still follow; nothing that resolves or changes the character.
        Assert.Equal(
            new[] { "Campaign.RecordJournalEntry", "Campaign.RecordPointOfInterest", "Campaign.SetPartyLocation" },
            Names(d => d.AllowedAfterTurnEnd));
    }

    /// <summary>Characters are made by the create-session and successor forms, never by the narrator, so a
    /// session cannot start without one and this stage is an unreachable fallback. It gets no tools at all:
    /// there is nothing it should be able to do, least of all conjure a character.</summary>
    [Fact]
    public void CharacterCreation_ExposesNoTools()
    {
        Assert.Empty(Flatten(SessionStage.CharacterCreation));
    }

    [Fact]
    public void CampaignSetup_ExposesConfigure()
    {
        Assert.Equal(
            new[] { "Campaign.ConfigureCampaign" },
            Flatten(SessionStage.CampaignSetup));
    }

    [Fact]
    public void Exploration_ExposesExactlyEighteenTools()
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
                "Character.GainSilver",
                "Character.GettingBetter",
                "Character.SellItem",
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
                "Character.GainSilver",
                "Character.GettingBetter",
                "Character.ImproveCharacterAbility",
                "Character.InfectCharacter",
                "Character.SellItem",
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
