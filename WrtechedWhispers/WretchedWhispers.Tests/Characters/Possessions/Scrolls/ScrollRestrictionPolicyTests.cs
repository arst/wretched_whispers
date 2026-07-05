using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Scrolls;

public class ScrollRestrictionPolicyTests
{
    [Fact]
    public void CanUseScrolls_OnlyAllowsIfWeaponIsNotTwoHandedAndArmorIsNoOrLight()
    {
        var weapon = Weapon.Create(WeaponKind.Knife);
        var armorNo = new Armor(ArmorTier.None);
        var armorLight = new Armor(ArmorTier.Light);
        var armorMedium = new Armor(ArmorTier.Medium);
        var armorHeavy = new Armor(ArmorTier.Heavy);

        Assert.True(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorNo));
        Assert.True(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorLight));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorMedium));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorHeavy));
    }

    [Fact]
    public void CanUseScrolls_DoesNotAllowIfWeaponIsTwoHandedRegardlessOfArmor()
    {
        var weapon = Weapon.Create(WeaponKind.Zweihander);
        var armorNo = new Armor(ArmorTier.None);
        var armorLight = new Armor(ArmorTier.Light);
        var armorHeavy = new Armor(ArmorTier.Heavy);

        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorNo));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorLight));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorHeavy));
    }
}