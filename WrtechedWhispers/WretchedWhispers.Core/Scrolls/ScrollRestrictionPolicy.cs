using WretchedWhispers.Core.Character.Armor;
using WretchedWhispers.Core.Character.Armor.Tiers;
using WretchedWhispers.Core.Character.Weapon;

namespace WretchedWhispers.Core.Scrolls;

public static class ScrollRestrictionPolicy
{
    public static bool CanUseScrolls(Weapon weapon, Armor armor)
    {
        return !weapon.IsTwoHanded && armor.Tier is NoArmorTier or LightArmorTier;
    }
}