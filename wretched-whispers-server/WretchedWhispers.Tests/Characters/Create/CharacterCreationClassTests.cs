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

    [Fact]
    public async Task FangedDeserter_AppliesAbilityBonusesAndFangs()
    {
        // Every d6 rolls 6, so each 3d6 is 18 -> a base modifier of +3 before class bonuses.
        SetupDiceRoll(6, 5);

        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.FangedDeserter);

        Assert.Equal(3 + 2, character.Abilities.Strength.Modifier);
        Assert.Equal(3 - 1, character.Abilities.Presence.Modifier);
        Assert.Equal(3, character.Abilities.Agility.Modifier);
        Assert.Equal(3, character.Abilities.Toughness.Modifier);
        // The natural attack replaces the rolled weapon outright.
        Assert.Equal(WeaponKind.Fangs, character.Weapon.Kind);
        Assert.Equal(DiceExpr.D4, character.Weapon.DamageDie);
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

    /// <summary>The crash case. A rolled -3 plus the Skinwalker's -2 Presence is -5, which AbilityScore
    /// rejects outright -- creation must clamp instead of throwing.</summary>
    [Fact]
    public async Task CursedSkinwalker_ClampsAbilityPenaltyInsteadOfThrowing()
    {
        // No d6 setup: every d6 rolls 1, so each 3d6 is 3 -> a base modifier of -3.
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.CursedSkinwalker);

        Assert.Equal(-3, character.Abilities.Presence.Modifier);
        Assert.Equal(-3 + 1, character.Abilities.Strength.Modifier);
        Assert.Equal(WeaponKind.Claws, character.Weapon.Kind);
        Assert.Equal(DiceExpr.D6, character.Weapon.DamageDie);
    }

    [Fact]
    public async Task CursedSkinwalker_StillHasAtLeastOneHitPoint()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.CursedSkinwalker);

        Assert.True(character.Hp.Max >= 1);
    }

    [Fact]
    public async Task EsotericHermit_StartsWithTwoUncleanScrollsAndAD6PowerDie()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.EsotericHermit);

        // All gear dice roll 1 (rope, life elixir), so neither gear slot contributes a scroll.
        Assert.Equal(2, character.Scrolls.Count);
        Assert.All(character.Scrolls, s => Assert.Equal(ScrollSchool.Unclean, s.School));
        Assert.Equal(DiceExpr.D6, character.Powers.PowerDie);
    }

    [Fact]
    public async Task HereticalPriest_StartsWithOneSacredScroll()
    {
        var character = await NewService().Create("Hero", Difficulty.Grim, CharacterClass.HereticalPriest);

        Assert.Single(character.Scrolls);
        Assert.Equal(ScrollSchool.Sacred, character.Scrolls[0].School);
    }

    /// <summary>Starting able to cast costs armour, whether the scroll came from the gear table or the class.</summary>
    [Fact]
    public async Task ClassGrantedScrolls_LimitTheArmourRoll()
    {
        // Every d4 rolls 4. Armour is a d4 normally but a d2 for a wretch who begins with a scroll, so a
        // classless character reaches Heavy while the Hermit is capped at Light.
        SetupDiceRoll(4, 3);
        SetupDiceRoll(2, 1);

        var classless = await NewService().Create("Hero", Difficulty.Grim);
        var hermit = await NewService().Create("Hermit", Difficulty.Grim, CharacterClass.EsotericHermit);

        Assert.Equal(ArmorTier.Heavy, classless.Armor.Tier);
        Assert.Equal(ArmorTier.Light, hermit.Armor.Tier);
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
    public async Task ClasslessKit_IsEmptyAndConsumesNoDice()
    {
        // Two rolled gear slots and nothing else -- no class kit means no extra items.
        var character = await NewService().Create("Hero", Difficulty.Grim);

        Assert.Equal(2, character.Inventory.InventoryItems.Count);
    }

    [Fact]
    public void RollRandomClass_NeverReturnsClassless()
    {
        var service = NewService();

        for (var i = 0; i < 20; i++)
            Assert.NotEqual(CharacterClass.Classless, service.RollRandomClass());
    }

    [Fact]
    public void RollRandomClass_MapsTheLowestRollToTheFirstRollableClass()
    {
        var character = NewService().RollRandomClass(); // every die rolls 1

        Assert.Equal(ClassPresets.Rollable[0], character);
    }
}
