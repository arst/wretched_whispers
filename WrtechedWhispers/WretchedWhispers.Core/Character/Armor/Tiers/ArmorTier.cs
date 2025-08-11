using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Character.Armor.Tiers;

public abstract class ArmorTier
{
    public abstract int DefencePenalty { get; }
    
    public abstract int AgilityPenalty { get; }

    public abstract DiceExpr DamageReduction { get; }
}