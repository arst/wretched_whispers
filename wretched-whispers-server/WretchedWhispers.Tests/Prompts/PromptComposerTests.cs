using Xunit;
using WretchedWhispers.Engine.Prompts;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Tests.Prompts;

public class PromptComposerTests : TestBase
{
    private readonly PromptComposer _composer = new();

    [Fact]
    public void Compose_includes_narrator_persona_text()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        var result = _composer.Compose(context);

        Assert.Contains("doom metal", result, StringComparison.OrdinalIgnoreCase);
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

        var expectedInstructions = StagePrompts.For(stage);
        Assert.Contains(expectedInstructions, result);
    }

    [Fact]
    public void Compose_includes_context_snapshot()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        var result = _composer.Compose(context);

        // Snapshot may be empty for empty context, but compose should still work
        Assert.NotNull(result);
        Assert.Contains("Game State", result);
    }

    [Fact]
    public void Compose_for_CharacterCreation_mentions_character_creation_tools()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        // No character = CharacterCreation stage

        var result = _composer.Compose(context);

        Assert.Contains("CreateCharacter", result);
    }

    [Fact]
    public void Compose_for_Combat_mentions_combat_resolution()
    {
        var context = BuildContextForStage(SessionStage.Combat);

        var result = _composer.Compose(context);

        Assert.Contains("Combat", result);
        Assert.Contains("ResolveCombatRound", result);
    }

    [Fact]
    public void Compose_for_Combat_treats_questions_as_not_actions()
    {
        var context = BuildContextForStage(SessionStage.Combat);

        var result = _composer.Compose(context);

        Assert.Contains("A question is not a combat round", result);
        Assert.Contains("Call no tools", result);
    }

    [Fact]
    public void Compose_requires_inventory_check_before_using_items()
    {
        var context = BuildContextForStage(SessionStage.Exploration);

        var result = _composer.Compose(context);

        Assert.Contains("first check the Game State", result);
        Assert.Contains("Do NOT invent random possessions", result);
    }

    [Fact]
    public void Compose_for_Combat_denies_missing_item_without_enemy_retaliation()
    {
        var context = BuildContextForStage(SessionStage.Combat);

        var result = _composer.Compose(context);

        Assert.Contains("first verify the item/resource exists", result);
        Assert.Contains("explain in-world and STOP", result);
    }

    [Fact]
    public void Compose_snapshot_includes_inventory_and_equipment()
    {
        var context = BuildContextForStage(SessionStage.Combat);

        var result = _composer.Compose(context);

        Assert.Contains("Weapon:", result);
        Assert.Contains("Armor:", result);
        Assert.Contains("Inventory", result);
        Assert.Contains("Powers:", result);
        Assert.Contains("Omens:", result);
    }

    [Fact]
    public void Compose_for_Ended_instructs_farewell_narration()
    {
        var context = BuildContextForStage(SessionStage.Ended);

        var result = _composer.Compose(context);

        Assert.Contains("Do not call any tools", result);
    }

    [Fact]
    public void Compose_includes_difficulty_tone_note()
    {
        var context = BuildContextForStage(SessionStage.Exploration);

        var result = _composer.Compose(context);

        Assert.Contains("Difficulty: GRIM", result);
    }

    [Fact]
    public void NarratorPersona_Text_contains_doom_metal_tone_guidance()
    {
        Assert.Contains("doom metal", NarratorPersona.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NEVER output raw JSON", NarratorPersona.Text);
    }

    [Theory]
    [InlineData(SessionStage.CharacterCreation)]
    [InlineData(SessionStage.CampaignSetup)]
    [InlineData(SessionStage.Exploration)]
    [InlineData(SessionStage.Combat)]
    [InlineData(SessionStage.Resolution)]
    [InlineData(SessionStage.Ended)]
    public void StagePrompts_For_returns_non_empty_string_for_all_stages(SessionStage stage)
    {
        var result = StagePrompts.For(stage);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    private SessionContext BuildContextForStage(SessionStage targetStage)
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        switch (targetStage)
        {
            case SessionStage.CharacterCreation:
                break;

            case SessionStage.CampaignSetup:
                context.SetCharacterId(Guid.NewGuid());
                context.Character = CreateMinimalCharacter();
                break;

            case SessionStage.Exploration:
                context.SetCharacterId(Guid.NewGuid());
                context.Character = CreateMinimalCharacter();
                context.SetCampaignId(Guid.NewGuid());
                context.Campaign = CreateActiveCampaign();
                break;

            case SessionStage.Combat:
                context.SetCharacterId(Guid.NewGuid());
                context.Character = CreateMinimalCharacter();
                context.SetCampaignId(Guid.NewGuid());
                context.Campaign = CreateActiveCampaign();
                context.SetActiveEncounterId(Guid.NewGuid());
                context.ActiveEncounter = CreateStartedEncounter();
                break;

            case SessionStage.Resolution:
                context.SetCharacterId(Guid.NewGuid());
                context.Character = CreateMinimalCharacter();
                context.SetCampaignId(Guid.NewGuid());
                context.Campaign = CreateActiveCampaign();
                context.SetActiveEncounterId(Guid.NewGuid());
                context.ActiveEncounter = CreateEndedUnresolvedEncounter();
                break;

            case SessionStage.Ended:
                context.SetCharacterId(Guid.NewGuid());
                context.Character = CreateMinimalCharacter();
                context.SetCampaignId(Guid.NewGuid());
                context.Campaign = CreateEndedCampaign();
                break;
        }

        return context;
    }

    private static Campaign CreateActiveCampaign()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        campaign.JoinGame(Guid.NewGuid());
        campaign.Start();
        return campaign;
    }

    [Fact]
    public void Compose_includes_the_class_narrator_note_for_a_classed_character()
    {
        var context = ContextForCharacterClass(CharacterClass.CursedSkinwalker);

        var result = _composer.Compose(context);

        Assert.Contains("## Class", result);
        Assert.Contains(ClassPresets.For(CharacterClass.CursedSkinwalker).NarratorNote, result);
    }

    /// <summary>Class flavour is the one part of the prompt with no domain state behind it, so it carries an
    /// explicit reminder that it grants nothing on its own.</summary>
    [Fact]
    public void Compose_class_section_forbids_the_class_from_granting_anything()
    {
        var result = _composer.Compose(ContextForCharacterClass(CharacterClass.OccultHerbmaster));

        Assert.Contains("never arithmetic", result);
        Assert.Contains("NEVER grants an item", result);
    }

    /// <summary>Classless wretches -- every character created before classes existed -- must produce the same
    /// prompt they always did.</summary>
    [Fact]
    public void Compose_omits_the_class_section_for_a_classless_character()
    {
        var result = _composer.Compose(ContextForCharacterClass(CharacterClass.Classless));

        Assert.DoesNotContain("## Class", result);
    }

    [Fact]
    public void Compose_omits_the_class_section_when_no_character_exists()
    {
        var result = _composer.Compose(new SessionContext { SessionId = Guid.NewGuid() });

        Assert.DoesNotContain("## Class", result);
    }

    [Fact]
    public void Snapshot_names_the_class_only_when_there_is_one()
    {
        Assert.Contains("Class: Cursed Skinwalker",
            ContextForCharacterClass(CharacterClass.CursedSkinwalker).FormatSnapshot());
        Assert.DoesNotContain("Class:",
            ContextForCharacterClass(CharacterClass.Classless).FormatSnapshot());
    }

    /// <summary>The creation script must actually name every class it offers, or the narrator invents its own.</summary>
    [Theory]
    [InlineData("Fanged Deserter")]
    [InlineData("Gutterborn Scum")]
    [InlineData("Esoteric Hermit")]
    [InlineData("Occult Herbmaster")]
    [InlineData("Heretical Priest")]
    [InlineData("Cursed Skinwalker")]
    public void CharacterCreation_prompt_offers_every_class(string displayName)
    {
        Assert.Contains(displayName, StagePrompts.For(SessionStage.CharacterCreation));
    }

    private SessionContext ContextForCharacterClass(CharacterClass characterClass)
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        var character = CreateClassedCharacter(characterClass);
        campaign.JoinGame(character.Id);
        campaign.Start();

        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.SetCharacterId(character.Id);
        context.Character = character;
        context.SetCampaignId(campaign.Id);
        context.Campaign = campaign;
        return context;
    }

    private Character CreateClassedCharacter(CharacterClass characterClass)
    {
        SetupDiceRolls(3);
        return Character.Create(
            Guid.NewGuid(), "Tuck", 2,
            new Abilities(new AbilityScore(0), new AbilityScore(0), new AbilityScore(1), new AbilityScore(0)),
            new StartingEquipment(120, 3, "Sack", null, null,
                Weapon.Create(WeaponKind.Staff), new Armor(ArmorTier.Medium), null, []),
            Dice, 0, characterClass);
    }

    private static Campaign CreateEndedCampaign()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "A test");
        campaign.JoinGame(Guid.NewGuid());
        campaign.Start();
        campaign.End();
        return campaign;
    }

    private Character CreateMinimalCharacter()
    {
        SetupDiceRolls(3);
        return Character.Create(
            Guid.NewGuid(),
            "Tuck",
            2,
            new Abilities(
                new AbilityScore(0),
                new AbilityScore(0),
                new AbilityScore(1),
                new AbilityScore(0)),
            new StartingEquipment(
                120,
                3,
                "Sack",
                new InventoryItem(Guid.NewGuid(), "Medicine chest", false, true, 5),
                null,
                Weapon.Create(WeaponKind.Staff),
                new Armor(ArmorTier.Medium),
                null,
                []),
            Dice);
    }

    private Encounter CreateStartedEncounter()
    {
        SetupDiceRolls(7); // For InitialReaction roll (2d6=7 => Indifferent)
        var encounter = Encounter.Create("Test", "A test", EncounterType.Hostile, Dice);
        var adversary = CreateMinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        return encounter;
    }

    private Encounter CreateEndedUnresolvedEncounter()
    {
        SetupDiceRolls(7); // For InitialReaction roll
        var encounter = Encounter.Create("Test", "A test", EncounterType.Hostile, Dice);
        var adversary = CreateMinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        KillAdversary(adversary);
        encounter.EndEncounter();
        // Not resolved - should be in Resolution stage
        return encounter;
    }

    private static Adversary CreateMinimalAdversary()
    {
        return new Adversary(
            "Goblin",
            new Core.Characters.HitPoints(5, 5),
            new Armor(ArmorTier.None),
            morale: 7,
            new AttackProfile("Claw", DiceExpr.D6));
    }

    private static void KillAdversary(Adversary adversary)
    {
        adversary.ReceiveDamage(1000);
    }
}
