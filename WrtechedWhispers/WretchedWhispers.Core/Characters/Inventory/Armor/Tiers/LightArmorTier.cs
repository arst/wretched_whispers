using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Inventory.Armor.Tiers;

public class LightArmorTier : ArmorTier
{
    public static readonly LightArmorTier Instance = new();

    private LightArmorTier()
    {
    }

    public override int DefencePenalty => 0;
    public override int AgilityPenalty => 0;
    public override DiceExpr DamageReduction => DiceExpr.D2;
}