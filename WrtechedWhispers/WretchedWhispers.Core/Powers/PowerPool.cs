using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Powers;

public sealed class PowerPool
{
    // Re-rolled each dawn: Presence + d4 uses
    public int UsesRemaining { get; private set; }
    public int MaxUses { get; set; }

    public void ResetForNewDay(AbilityScore presence)
    {
        MaxUses = Math.Max(0, presence.Modifier) + Dice.Roll(DiceExpr.D4);
        UsesRemaining = MaxUses;
    }

    public bool TryConsumeOne()
    {
        if (UsesRemaining <= 0) return false;
        UsesRemaining--;
        return true;
    }
}