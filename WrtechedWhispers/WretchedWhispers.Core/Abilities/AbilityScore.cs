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
}