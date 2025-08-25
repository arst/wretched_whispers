using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Inventory.Armor.Tiers;

public class HeavyArmorTier : ArmorTier
{
    public static readonly HeavyArmorTier Instance = new();

    private HeavyArmorTier()
    {
    }

    public override int DefencePenalty => 2;
    public override int AgilityPenalty => 4;
    public override DiceExpr DamageReduction => DiceExpr.D6;
}