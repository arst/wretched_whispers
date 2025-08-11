using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Test;

public sealed class Test
{
    public static TestResult Roll(IRandomService rng, AbilityScore ability, Dr dr)
    {
        var roll = rng.Roll(DiceExpr.d20);
        var total = roll + ability.Modifier;
        var outcome = total >= dr.Value ? TestOutcome.Success : TestOutcome.Fail;
        var nat = roll switch { 1 => Natural.One, 20 => Natural.Twenty, _ => Natural.None };
        return new TestResult(roll, total, dr.Value, ability.Modifier, outcome, nat);
    }
}