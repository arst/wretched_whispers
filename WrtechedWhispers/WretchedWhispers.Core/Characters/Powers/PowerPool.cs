using System.Text.Json.Serialization;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Powers;

public sealed class PowerPool
{
    [JsonConstructor]
    private PowerPool(int usesRemaining, int maxUses)
    {
        UsesRemaining = usesRemaining;
        MaxUses = maxUses;
    }

    private PowerPool() : this(0, 0)
    {
    }

    // Re-rolled each dawn: Presence + d4 uses
    [JsonInclude] public int UsesRemaining { get; private set; }
    [JsonInclude] public int MaxUses { get; private set; }

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