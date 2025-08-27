using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Powers;

public sealed class PowerPool
{
    private PowerPool() { }
    // Re-rolled each dawn: Presence + d4 uses
    public int UsesRemaining { get; private set; }
    public int MaxUses { get; private set; }

    public void ResetForNewDay(Abilities.Abilities abilities)
    {
        MaxUses = Math.Max(0, abilities.Presence.Modifier) + Dice.Roll(DiceExpr.D4);
        UsesRemaining = MaxUses;
    }

    public bool TryConsumeOne()
    {
        if (UsesRemaining <= 0) return false;
        UsesRemaining--;
        return true;
    }

    public static PowerPool Create(Abilities.Abilities abilities)
    {
        var pool = new PowerPool();
        pool.ResetForNewDay(abilities);
        return pool;
    }
}