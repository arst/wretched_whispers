using Xunit;
using WretchedWhispers.Core.Characters.Posessions.Scrolls;
using WretchedWhispers.Core.Characters.Posessions.Armor.Tiers;
using WretchedWhispers.Core.Characters.Posessions.Weapon;

namespace WretchedWhispers.Tests.Characters.Posessions.Scrolls;

public class ScrollRestrictionPolicyTests
{
    [Fact]
    public void CanUseScrolls_OnlyAllowsIfWeaponIsNotTwoHandedAndArmorIsNoOrLight()
    {
        var weapon = Core.Characters.Posessions.Weapon.Weapon.Create(WeaponKind.Knife);
        var armorNo = new Core.Characters.Posessions.Armor.Armor(NoArmorTier.Instance);
        var armorLight = new Core.Characters.Posessions.Armor.Armor(LightArmorTier.Instance);
        var armorMedium = new Core.Characters.Posessions.Armor.Armor(MediumArmorTier.Instance);
        var armorHeavy = new Core.Characters.Posessions.Armor.Armor(HeavyArmorTier.Instance);

        Assert.True(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorNo));
        Assert.True(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorLight));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorMedium));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorHeavy));
    }

    [Fact]
    public void CanUseScrolls_DoesNotAllowIfWeaponIsTwoHandedRegardlessOfArmor()
    {
        var weapon = Core.Characters.Posessions.Weapon.Weapon.Create(WeaponKind.Zweihander);
        var armorNo = new Core.Characters.Posessions.Armor.Armor(NoArmorTier.Instance);
        var armorLight = new Core.Characters.Posessions.Armor.Armor(LightArmorTier.Instance);
        var armorHeavy = new Core.Characters.Posessions.Armor.Armor(HeavyArmorTier.Instance);

        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorNo));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorLight));
        Assert.False(ScrollRestrictionPolicy.CanUseScrolls(weapon, armorHeavy));
    }
}

