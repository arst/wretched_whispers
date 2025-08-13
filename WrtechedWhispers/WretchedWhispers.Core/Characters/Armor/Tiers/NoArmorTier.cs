using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Characters.Armor.Tiers;

public class NoArmorTier : ArmorTier
{
    public static readonly NoArmorTier Instance = new();

    private NoArmorTier()
    {
    }

    public override int DefencePenalty => 0;
    public override int AgilityPenalty => 0;
    public override DiceExpr DamageReduction => new(0, 0);
}