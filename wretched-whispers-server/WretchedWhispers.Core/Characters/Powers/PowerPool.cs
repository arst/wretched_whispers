using System.Text.Json.Serialization;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Powers;

public sealed class PowerPool
{
    [JsonConstructor]
    private PowerPool(int usesRemaining, int maxUses, DiceExpr? powerDie = null)
    {
        UsesRemaining = usesRemaining;
        MaxUses = maxUses;
        PowerDie = powerDie;
    }

    private PowerPool(DiceExpr? powerDie = null) : this(0, 0, powerDie)
    {
    }

    // Re-rolled each dawn: Presence + PowerDie uses
    [JsonInclude] public int UsesRemaining { get; private set; }
    [JsonInclude] public int MaxUses { get; private set; }

    /// <summary>The class's daily power die. Stored rather than passed to <see cref="ResetForNewDay"/> so
    /// every dawn/rest caller stays untouched. Null for characters saved before classes existed, and for
    /// classes that do not change the die -- both fall back to d4, the original formula.
    /// Nullable so it round-trips through the deserialization constructor.</summary>
    [JsonInclude] public DiceExpr? PowerDie { get; private set; }

    public void ResetForNewDay(Abilities.Abilities abilities, Dice dice)
    {
        MaxUses = Math.Max(0, abilities.Presence.Modifier) + dice.Roll(PowerDie ?? DiceExpr.D4);
        UsesRemaining = MaxUses;
    }

    public bool TryConsumeOne()
    {
        if (UsesRemaining <= 0) return false;
        UsesRemaining--;
        return true;
    }

    public static PowerPool Create(Abilities.Abilities abilities, Dice dice, DiceExpr? powerDie = null)
    {
        var pool = new PowerPool(powerDie);
        pool.ResetForNewDay(abilities, dice);
        return pool;
    }
}