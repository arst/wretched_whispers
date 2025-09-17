using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

public class MediumArmorTier : ArmorTier
{
    public static readonly MediumArmorTier Instance = new();

    private MediumArmorTier()
    {
    }

    public override int DefencePenalty => 0;
    public override int AgilityPenalty => 2;
    public override DiceExpr DamageReduction => DiceExpr.D4;
}