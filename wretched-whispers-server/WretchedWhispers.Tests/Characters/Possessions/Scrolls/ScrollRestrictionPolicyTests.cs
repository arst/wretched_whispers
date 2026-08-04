using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Scrolls;

public class ScrollRestrictionPolicyTests
{
    [Theory]
    // One-handed weapon: armor decides -- only None or Light allows casting.
    [InlineData(WeaponKind.Knife, ArmorTier.None, true)]
    [InlineData(WeaponKind.Knife, ArmorTier.Light, true)]
    [InlineData(WeaponKind.Knife, ArmorTier.Medium, false)]
    [InlineData(WeaponKind.Knife, ArmorTier.Heavy, false)]
    // Two-handed weapon: never, regardless of armor.
    [InlineData(WeaponKind.Zweihander, ArmorTier.None, false)]
    [InlineData(WeaponKind.Zweihander, ArmorTier.Light, false)]
    [InlineData(WeaponKind.Zweihander, ArmorTier.Medium, false)]
    [InlineData(WeaponKind.Zweihander, ArmorTier.Heavy, false)]
    public void CanUseScrolls_RequiresFreeHandAndLightOrNoArmor(
        WeaponKind weaponKind, ArmorTier armorTier, bool expected)
    {
        var weapon = Weapon.Create(weaponKind);
        var armor = new Armor(armorTier);

        Assert.Equal(expected, ScrollRestrictionPolicy.CanUseScrolls(weapon, armor));
    }
}
