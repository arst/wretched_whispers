using WretchedWhispers.Core.Characters.Posessions.Armor;
using WretchedWhispers.Core.Characters.Posessions.Armor.Tiers;
using WretchedWhispers.Core.Characters.Posessions.Weapon;

namespace WretchedWhispers.Core.Scrolls;

public static class ScrollRestrictionPolicy
{
    public static bool CanUseScrolls(Weapon weapon, Armor armor)
    {
        return !weapon.IsTwoHanded && armor.Tier is NoArmorTier or LightArmorTier;
    }
}