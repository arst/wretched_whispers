using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Powers;

public sealed class PowerPool
{
    // Re-rolled each dawn: Presence + d4 uses
    public int UsesRemaining { get; private set; }
    public int MaxUses { get; set; }

    public void ResetForNewDay(Dice.Dice dice, AbilityScore presence)
    {
        MaxUses = Math.Max(0, presence.Modifier) + dice.Roll(DiceExpr.d4);
        UsesRemaining = MaxUses;
    }

    public bool TryConsumeOne()
    {
        if (UsesRemaining <= 0) return false;
        UsesRemaining--;
        return true;
    }
}