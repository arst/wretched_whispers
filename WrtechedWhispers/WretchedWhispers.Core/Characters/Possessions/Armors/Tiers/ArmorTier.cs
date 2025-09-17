using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

public abstract class ArmorTier
{
    public abstract int DefencePenalty { get; }

    public abstract int AgilityPenalty { get; }

    public abstract DiceExpr DamageReduction { get; }
}