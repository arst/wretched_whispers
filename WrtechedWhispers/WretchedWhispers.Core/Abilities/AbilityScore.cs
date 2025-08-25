using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Abilities;

public readonly record struct AbilityScore
{
    public AbilityScore(int value)
    {
        Validate(value);
        Modifier = value;
    }

    public int Modifier { get; }

    public override string ToString()
    {
        return Modifier >= 0 ? $"+{Modifier}" : Modifier.ToString();
    }

    private static void Validate(int value)
    {
        if (value is > 6 or < -3) throw new InvalidOperationException("Ability value must be between -3 and +6.");
    }

    public AbilityTestResult Test(Dr dr)
    {
        var roll = Dice.Roll(DiceExpr.D20);
        var total = roll + Modifier;
        var outcome = total >= dr.Value ? TestOutcome.Success : TestOutcome.Fail;
        var nat = roll switch { 1 => Natural.One, 20 => Natural.Twenty, _ => Natural.None };
        return new AbilityTestResult(roll, total, dr.Value, Modifier, outcome, nat);
    }
}