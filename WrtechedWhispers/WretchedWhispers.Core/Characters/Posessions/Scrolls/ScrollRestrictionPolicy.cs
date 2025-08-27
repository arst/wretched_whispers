using WretchedWhispers.Core.Characters.Posessions.Armor.Tiers;

namespace WretchedWhispers.Core.Characters.Posessions.Scrolls;

public static class ScrollRestrictionPolicy
{
    public static bool CanUseScrolls(Weapon.Weapon weapon, Armor.Armor armor)
    {
        return !weapon.IsTwoHanded && armor.Tier is NoArmorTier or LightArmorTier;
    }
}