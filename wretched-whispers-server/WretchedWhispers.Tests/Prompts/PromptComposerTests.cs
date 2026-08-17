using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.Prompts;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Prompts;

public class PromptComposerTests : TestBase
{
    private readonly PromptComposer _composer = new();

    [Fact]
    public void Compose_includes_narrator_persona_text()
    {
        var result = _composer.Compose(new SessionContext { SessionId = Guid.NewGuid() });

        Assert.Contains(NarratorPersona.Text, result);
    }

    [Theory]
    [InlineData(SessionStage.CharacterCreation)]
    [InlineData(SessionStage.CampaignSetup)]
    [InlineData(SessionStage.Exploration)]
    [InlineData(SessionStage.Combat)]
    [InlineData(SessionStage.Resolution)]
    [InlineData(SessionStage.Ended)]
    public void Compose_includes_stage_specific_instructions(SessionStage stage)
    {
        var context = BuildContextForStage(stage);

        var result = _composer.Compose(context);

        Assert.Contains(StagePrompts.For(stage), result);
    }

    [Fact]
    public void Compose_includes_difficulty_tone_note()
    {
        var context = BuildContextForStage(SessionStage.Exploration);

        var result = _composer.Compose(context);

        Assert.Contains(DifficultyPresets.For(Difficulty.Grim).GmToneNote, result);
    }

    [Fact]
    public void Compose_includes_the_class_narrator_note_for_a_classed_character()
    {
        var context = BuildContextForStage(SessionStage.Exploration, CharacterClass.CursedSkinwalker);

        var result = _composer.Compose(context);

        Assert.Contains("## Class", result);
        Assert.Contains(ClassPresets.For(CharacterClass.CursedSkinwalker).NarratorNote, result);
    }

    /// <summary>Classless wretches -- every character created before classes existed -- must produce the same
    /// prompt they always did.</summary>
    [Fact]
    public void Compose_omits_the_class_section_for_a_classless_character()
    {
        var result = _composer.Compose(BuildContextForStage(SessionStage.Exploration));

        Assert.DoesNotContain("## Class", result);
    }

    [Fact]
    public void Compose_omits_the_class_section_when_no_character_exists()
    {
        var result = _composer.Compose(new SessionContext { SessionId = Guid.NewGuid() });

        Assert.DoesNotContain("## Class", result);
    }

    // -- helpers --

    private SessionContext BuildContextForStage(SessionStage targetStage,
        CharacterClass characterClass = CharacterClass.Classless)
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        if (targetStage == SessionStage.CharacterCreation)
            return context;

        var character = CreateCharacter(characterClass);
        context.SetCharacterId(character.Id);
        context.Character = character;
        if (targetStage == SessionStage.CampaignSetup)
            return context;

        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        campaign.JoinGame(character.Id);
        campaign.Start();
        if (targetStage == SessionStage.Ended)
            campaign.End();
        context.SetCampaignId(campaign.Id);
        context.Campaign = campaign;

        if (targetStage is SessionStage.Combat or SessionStage.Resolution)
        {
            var encounter = CreateStartedEncounter(ended: targetStage == SessionStage.Resolution);
            context.SetActiveEncounterId(encounter.Id);
            context.ActiveEncounter = encounter;
        }

        return context;
    }

    /// <summary>The shared TestCharacters builder has no class knob, so this one local helper delegates
    /// to Character.Create directly. Consumes one scripted roll at construction.</summary>
    private Character CreateCharacter(CharacterClass characterClass)
    {
        SetupDiceRolls(3);
        return Character.Create(
            Guid.NewGuid(), "Tuck", 2,
            new Abilities(new AbilityScore(0), new AbilityScore(0), new AbilityScore(1), new AbilityScore(0)),
            new StartingEquipment(120, 3, "Sack", null, null,
                Weapon.Create(WeaponKind.Staff), new Armor(ArmorTier.Medium), null, []),
            Dice, 0, characterClass);
    }

    /// <summary>Started encounter; when <paramref name="ended"/>, the adversary is killed and the
    /// encounter ended but left unresolved, which derives to the Resolution stage.</summary>
    private Encounter CreateStartedEncounter(bool ended = false)
    {
        SetupDiceRolls(7); // For InitialReaction roll
        var encounter = Encounter.Create("Test", "A test", EncounterType.Hostile, Dice);
        var adversary = new Adversary(
            "Goblin", new HitPoints(5, 5), new Armor(ArmorTier.None), morale: 7,
            new AttackProfile("Claw", DiceExpr.D6));
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        if (ended)
        {
            adversary.ReceiveDamage(1000);
            encounter.EndEncounter();
        }

        return encounter;
    }
}
