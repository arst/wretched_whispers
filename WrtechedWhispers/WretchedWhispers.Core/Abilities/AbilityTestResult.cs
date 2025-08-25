namespace WretchedWhispers.Core.Abilities;

public readonly record struct AbilityTestResult(
    int Roll,
    int Total,
    int TargetDr,
    int AbilityMod,
    TestOutcome Outcome,
    Natural Natural
)
{
    public bool IsCrit => Natural == Natural.Twenty;
    public bool IsFumble => Natural == Natural.One;
}