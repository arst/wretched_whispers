using WretchedWhispers.Api.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

/// <summary>
/// Characterization tests pinning the stage -> tool allow-list. These are framework-agnostic
/// (no Semantic Kernel / Agent Framework types) and act as the regression oracle for the
/// SK→Agent Framework migration: the set of tools exposed per stage must not change.
/// </summary>
public class StageToolMapTests
{
    private static string[] Flatten(SessionStage stage) =>
        StageToolMap.Map.TryGetValue(stage, out var plugins)
            ? plugins.SelectMany(p => p.Value.Select(fn => $"{p.Key}.{fn}")).OrderBy(x => x).ToArray()
            : [];

    [Fact]
    public void CharacterCreation_ExposesCreateCharacterAndCampaignSetupTools()
    {
        // The opening stage runs the whole intro in one turn (create character + configure/start
        // the campaign), so it also exposes the campaign-setup tools.
        Assert.Equal(
            new[] { "Campaign.ConfigureCampaign", "Campaign.StartCampaign", "Character.CreateCharacter" },
            Flatten(SessionStage.CharacterCreation));
    }

    [Fact]
    public void CampaignSetup_ExposesConfigureAndStart()
    {
        Assert.Equal(
            new[] { "Campaign.ConfigureCampaign", "Campaign.StartCampaign" },
            Flatten(SessionStage.CampaignSetup));
    }

    [Fact]
    public void Exploration_ExposesExactlyTenTools()
    {
        var tools = Flatten(SessionStage.Exploration);
        Assert.Equal(10, tools.Length);
        Assert.Contains("Character.ChallengeCharacter", tools);
        Assert.Contains("Encounter.CreateEncounter", tools);
        Assert.Contains("Dice.Roll", tools);
        Assert.DoesNotContain("Character.CreateCharacter", tools);
        Assert.DoesNotContain("Encounter.AttackPlayer", tools);
    }

    [Fact]
    public void Combat_ExposesOnlyCombatTools()
    {
        Assert.Equal(
            new[] { "Dice.Roll", "Encounter.AttackAdversary", "Encounter.AttackPlayer", "Encounter.EndEncounter" },
            Flatten(SessionStage.Combat));
    }

    [Fact]
    public void Resolution_ExposesResolutionToolsAndNoCreation()
    {
        var tools = Flatten(SessionStage.Resolution);
        Assert.Contains("Resolution.CompleteResolution", tools);
        Assert.Contains("Campaign.AdvanceTime", tools);
        Assert.Contains("Character.AddItemToCharacterInventory", tools);
        Assert.DoesNotContain("Character.CreateCharacter", tools);
        Assert.DoesNotContain("Campaign.StartCampaign", tools);
        Assert.DoesNotContain("Encounter.AttackPlayer", tools);
    }

    [Fact]
    public void Ended_ExposesNoTools()
    {
        Assert.Empty(Flatten(SessionStage.Ended));
    }
}
