namespace WretchedWhispers.Core.Outcomes;

public abstract record BrokenOutcome(string Kind)
{
    public static BrokenOutcome Unconscious(int rounds, int awakenHp)
    {
        return new UnconsciousBroken(rounds, awakenHp);
    }

    public static BrokenOutcome BrokenOrSeveredLimb(int rounds)
    {
        return new LimbBroken(rounds);
    }

    public static BrokenOutcome LostEye(int rounds)
    {
        return new EyeLost(rounds);
    }

    public static BrokenOutcome Hemorrhage()
    {
        return new HemorrhageBroken();
    }

    public static BrokenOutcome Dead()
    {
        return new DeadBroken();
    }
}