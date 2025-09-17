using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;

namespace WretchedWhispers.Core.Characters.Possessions.Scrolls;

public static class ScrollRestrictionPolicy
{
    public static bool CanUseScrolls(Weapon weapon, Armor armor)
    {
        return !weapon.IsTwoHanded && armor.Tier is NoArmorTier or LightArmorTier;
    }
}