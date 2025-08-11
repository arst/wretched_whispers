using WretchedWhispers.Core.Abilities;

namespace WretchedWhispers.Core.Powers;

public sealed class PowerPool
{
    // Re-rolled each dawn: Presence + d4 uses
    public int UsesRemaining { get; private set; }

    public void ResetForNewDay(IRandomService rng, AbilityScore presence)
    {
        UsesRemaining = Math.Max(0, presence.Modifier) + rng.D(4);
    }

    public bool TryConsumeOne()
    {
        if (UsesRemaining <= 0) return false;
        UsesRemaining--;
        return true;
    }
}