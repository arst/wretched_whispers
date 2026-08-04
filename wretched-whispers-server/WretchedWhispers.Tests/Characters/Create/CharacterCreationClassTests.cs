using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Create;

/// <summary>Class effects at creation. Unmocked die sizes return the mock default (roll of 1), so any die
/// not explicitly set up here is deterministic at its minimum.</summary>
public class CharacterCreationClassTests : TestBase
{
    private CharacterCreationService NewService() =>
        new(new Mock<ICharactersRepository>().Object, Dice);

    [Fact]
    public async Task Create_DefaultsToClassless()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.Equal(CharacterClass.Classless, character.Class);
    }

    [Fact]
    public async Task Create_StoresTheChosenClass()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.HereticalPriest);

        Assert.Equal(CharacterClass.HereticalPriest, character.Class);
    }

    /// <summary>Bonuses land on the 3d6 roll, not on the modifier it maps to. Rolling 15 puts the class
    /// either side of a boundary: Strength +2 crosses into +3, the two -1s drop to +1, and an untouched
    /// ability stays at +2. Adding to the modifier instead would read +4/+1/+1/+2 here.</summary>
    [Fact]
    public async Task FangedDeserter_AppliesAbilityBonusesToTheRollAndFightsWithFangs()
    {
        SetupDiceRoll(6, 4); // every d6 rolls 5, so each 3d6 is 15 -> a base modifier of +2

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.FangedDeserter);

        Assert.Equal(+3, character.Abilities.Strength.Modifier); // 15 + 2 = 17
        Assert.Equal(+1, character.Abilities.Presence.Modifier); // 15 - 1 = 14
        Assert.Equal(+1, character.Abilities.Agility.Modifier); // 15 - 1 = 14
        Assert.Equal(+2, character.Abilities.Toughness.Modifier); // untouched
        // The natural attack replaces the rolled weapon outright.
        Assert.Equal(WeaponKind.Fangs, character.Weapon.Kind);
        Assert.Equal(DiceExpr.D6, character.Weapon.DamageDie);
    }

    /// <summary>Illiterate: the deserter rolls the same gear tables, but a scroll is paper to them, so it
    /// lands in the pack instead of the spell list -- and costs them none of the armour a caster pays.</summary>
    [Fact]
    public async Task FangedDeserter_CannotReadTheScrollItRolls()
    {
        SetupDiceRoll(12, 4); // both gear slots roll 5 -> an unclean scroll, and perfume
        SetupDiceRoll(4, 3); // armour d4 rolls 4 -> Heavy, if nothing caps it

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.FangedDeserter);

        Assert.Empty(character.Scrolls);
        Assert.Contains(character.Inventory.InventoryItems, i => i.Description == "random unclean scroll");
        Assert.Equal(ArmorTier.Heavy, character.Armor.Tier);
    }

    [Fact]
    public async Task FangedDeserter_RollsHpOnAD10()
    {
        // 3d6 of 6s -> Toughness +3; d10 rolls 10. Grim adds no starting HP bonus.
        SetupDiceRoll(6, 5);
        SetupDiceRoll(10, 9);

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.FangedDeserter);

        Assert.Equal(3 + 10, character.Hp.Max);
    }

    /// <summary>The old crash case, now unreachable by construction. A -2 on a rolled 3 is a roll of 1,
    /// which the modifier table floors at -3 -- so no clamp is needed to keep AbilityScore's -3..+6.</summary>
    [Fact]
    public async Task CursedSkinwalker_PenaltyOnTheRollCannotEscapeTheModifierFloor()
    {
        // No d6 setup: every d6 rolls 1, so each 3d6 is 3 -> a base modifier of -3.
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.CursedSkinwalker);

        Assert.Equal(-3, character.Abilities.Presence.Modifier);
        Assert.Equal(-3, character.Abilities.Strength.Modifier);
        Assert.Equal(WeaponKind.Claws, character.Weapon.Kind);
        Assert.Equal(DiceExpr.D6, character.Weapon.DamageDie);
    }

    /// <summary>One scroll, and the school is rolled rather than fixed -- the hermit read whatever was in
    /// the hole with them.</summary>
    [Fact]
    public async Task EsotericHermit_StartsWithOneScrollOfARolledSchool()
    {
        // All gear dice roll 1 (rope, life elixir), so neither gear slot contributes a scroll of its own.
        var sacred = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.EsotericHermit);

        Assert.Single(sacred.Scrolls);
        Assert.Equal(ScrollSchool.Sacred, sacred.Scrolls[0].School); // every d2 rolls 1

        SetupDiceRoll(2, 1); // every d2 rolls 2
        var unclean = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.EsotericHermit);

        Assert.Equal(ScrollSchool.Unclean, unclean.Scrolls[0].School);
    }

    /// <summary>The hermit is the worst-armed class in the game: a d4 weapon table stops at the knife.</summary>
    [Fact]
    public async Task EsotericHermit_RollsWeaponsOnAD4()
    {
        SetupDiceRoll(4, 3); // every d4 rolls 4
        SetupDiceRoll(10, 9); // a d10 would reach the zweihander, if the class could roll one

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.EsotericHermit);

        Assert.Equal(WeaponKind.Knife, character.Weapon.Kind);
    }

    /// <summary>No free scroll for the priest -- their edge is omens, silver and a relic we do not model.</summary>
    [Fact]
    public async Task HereticalPriest_HasNoStartingScrollButFourOmens()
    {
        SetupDiceRoll(4, 3); // the omen d4 rolls 4

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.HereticalPriest);

        Assert.Empty(character.Scrolls);
        Assert.Equal(4, character.Omens.Count);
    }

    /// <summary>Silver is a class number too: the priest passes a plate around, the hermit does not.</summary>
    [Theory]
    [InlineData(CharacterClass.HereticalPriest, 180)] // 3d6 x 10
    [InlineData(CharacterClass.EsotericHermit, 60)] // 1d6 x 10
    public async Task StartingSilver_FollowsTheClassDice(CharacterClass characterClass, int expectedSilver)
    {
        SetupDiceRoll(6, 5); // every d6 rolls 6

        var character = await NewService().Create("Hero", Difficulty.Grim, characterClass);

        Assert.Equal(expectedSilver, character.Silver);
    }

    /// <summary>Starting able to cast costs armour, and the cap bites a class the tables would otherwise
    /// let wear plate.</summary>
    [Fact]
    public async Task ARolledScroll_CapsTheArmourOfAClassAllowedHeavy()
    {
        SetupDiceRoll(4, 3); // every d4 rolls 4 -> Heavy
        SetupDiceRoll(2, 1); // every d2 rolls 2 -> Light
        SetupDiceRoll(12, 0); // both gear slots roll 1 -> no scroll

        var uncapped = await NewService().Create("Priest", Difficulty.Grim, CharacterClass.HereticalPriest);

        SetupDiceRoll(12, 4); // gear slot 1 rolls 5 -> a random unclean scroll
        var capped = await NewService().Create("Priest", Difficulty.Grim, CharacterClass.HereticalPriest);

        Assert.Empty(uncapped.Scrolls);
        Assert.Equal(ArmorTier.Heavy, uncapped.Armor.Tier);
        Assert.Single(capped.Scrolls);
        Assert.Equal(ArmorTier.Light, capped.Armor.Tier);
    }

    [Fact]
    public async Task GutterbornScum_StartsWithTrinkets()
    {
        // d4 rolls 1 -> one trinket; d8 rolls 1 -> the first entry on the trinket table.
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.GutterbornScum);

        Assert.Contains(character.Inventory.InventoryItems,
            i => i.Description == "a child's tooth on a string");
    }

    [Fact]
    public async Task GutterbornScum_TrinketCountFollowsTheD4()
    {
        SetupDiceRoll(4, 3); // every d4 rolls 4 -> four trinkets
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.GutterbornScum);

        Assert.Equal(4, character.Inventory.InventoryItems
            .Count(i => i.Description == "a child's tooth on a string"));
    }

    [Fact]
    public async Task OccultHerbmaster_StartsWithAPouchAndHerbs()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.OccultHerbmaster);

        Assert.Contains(character.Inventory.InventoryItems, i => i.Description == "herb pouch & pestle");
        Assert.Contains(character.Inventory.InventoryItems, i => i.Description.StartsWith("gravebloom"));
        // Herbs are consumed when used; the pouch is not.
        Assert.True(character.Inventory.InventoryItems
            .Single(i => i.Description.StartsWith("gravebloom")).IsOneTimeUse);
    }

    [Fact]
    public async Task ClasslessKit_AddsNoItems()
    {
        // Two rolled gear slots and nothing else -- no class kit means no extra items.
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.Equal(2, character.Inventory.InventoryItems.Count);
    }

    [Fact]
    public void RollRandomClass_NeverReturnsClassless()
    {
        // The mock die always rolls 1, so a single call covers the deterministic mapping.
        Assert.NotEqual(CharacterClass.Classless, NewService().RollRandomClass());
    }

    [Fact]
    public void RollRandomClass_MapsTheLowestRollToTheFirstRollableClass()
    {
        var rolledClass = NewService().RollRandomClass(); // every die rolls 1

        Assert.Equal(ClassPresets.Rollable[0], rolledClass);
    }
}
