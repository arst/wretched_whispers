using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions;

/// <summary>What a character wields and wears was fixed at creation: looted mail or a bought sword could
/// only be narrated, and the sheet never changed.</summary>
public class EquipmentTests : TestBase
{
    [Fact]
    public void Equip_ReplacesTheWeapon()
    {
        var hero = TestCharacters.Create(Dice, weaponKind: WeaponKind.Knife);

        hero.Equip(Weapon.Create(WeaponKind.Flail));

        Assert.Equal(WeaponKind.Flail, hero.Weapon.Kind);
        Assert.Equal(10, hero.Silver);
    }

    [Fact]
    public void Wear_ReplacesTheArmor()
    {
        var hero = TestCharacters.Create(Dice, armorTier: ArmorTier.None);

        hero.Wear(new Armor(ArmorTier.Medium));

        Assert.Equal(ArmorTier.Medium, hero.Armor.Tier);
    }

    [Fact]
    public void TakeUpShield_GivesAShield()
    {
        var hero = TestCharacters.Create(Dice);
        Assert.Null(hero.Shield);

        hero.TakeUpShield();

        Assert.NotNull(hero.Shield);
    }

    [Fact]
    public void Equip_WithAPrice_DeductsSilver()
    {
        var hero = TestCharacters.Create(Dice, weaponKind: WeaponKind.Knife);

        hero.Equip(Weapon.Create(WeaponKind.Sword), price: 7);

        Assert.Equal(WeaponKind.Sword, hero.Weapon.Kind);
        Assert.Equal(3, hero.Silver);
    }

    [Fact]
    public void Gear_PricedAboveTheCharactersSilver_ThrowsAndChangesNothing()
    {
        var hero = TestCharacters.Create(Dice, weaponKind: WeaponKind.Knife, armorTier: ArmorTier.None);

        Assert.Throws<InvalidOperationException>(() => hero.Equip(Weapon.Create(WeaponKind.Sword), price: 11));
        Assert.Throws<InvalidOperationException>(() => hero.Wear(new Armor(ArmorTier.Heavy), price: 11));
        Assert.Throws<InvalidOperationException>(() => hero.TakeUpShield(price: 11));

        Assert.Equal(WeaponKind.Knife, hero.Weapon.Kind);
        Assert.Equal(ArmorTier.None, hero.Armor.Tier);
        Assert.Null(hero.Shield);
        Assert.Equal(10, hero.Silver);
    }

    // The restriction existed, but no play could reach it: armor never changed after creation.
    [Fact]
    public void Wear_HeavyArmor_BlocksScrolls()
    {
        SetupDiceRolls(0, 14); // d4 daily powers; the d20 a permitted casting would roll
        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech");
        var hero = TestCharacters.Create(Dice, scrolls: scroll);

        hero.Wear(new Armor(ArmorTier.Heavy));
        var outcome = hero.Cast(scroll.Id, Dice);

        Assert.False(outcome.Succeeded);
        Assert.Contains("armor is too heavy", outcome.Reason);
    }

    [Theory]
    [InlineData(WeaponKind.Fangs)]
    [InlineData(WeaponKind.Claws)]
    public void Equip_ANaturalAttackTheClassDoesNotHave_IsRefused(WeaponKind natural)
    {
        var hero = TestCharacters.Create(Dice, weaponKind: WeaponKind.Knife);

        Assert.Throws<InvalidOperationException>(() => hero.Equip(Weapon.Create(natural)));
        Assert.Equal(WeaponKind.Knife, hero.Weapon.Kind);
    }

    // A wretch born with fangs can put the sword down again.
    [Theory]
    [InlineData(CharacterClass.FangedDeserter)]
    [InlineData(CharacterClass.CursedSkinwalker)]
    public void Equip_TheClassesOwnNaturalAttack_IsAllowed(CharacterClass characterClass)
    {
        var natural = ClassPresets.For(characterClass).NaturalWeapon;
        Assert.NotNull(natural);
        var abilities = new WretchedWhispers.Core.Characters.Abilities.Abilities(
            new(0), new(0), new(0), new(0));
        var hero = Character.Create(Guid.NewGuid(), "Born Armed", 10, abilities,
            new StartingEquipment(10, 1, "sack", null, null, Weapon.Create(natural.Value),
                new Armor(ArmorTier.None), null, []), Dice, characterClass: characterClass);

        hero.Equip(Weapon.Create(WeaponKind.Sword));
        hero.Equip(Weapon.Create(natural.Value));

        Assert.Equal(natural.Value, hero.Weapon.Kind);
    }
}
