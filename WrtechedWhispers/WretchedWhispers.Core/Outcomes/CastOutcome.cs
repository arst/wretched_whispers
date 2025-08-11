namespace WretchedWhispers.Core.Outcomes;

public readonly record struct CastOutcome(bool Succeeded, string Reason, string? PowerKey, int HpLost)
{
    public static CastOutcome Success(string key)
    {
        return new CastOutcome(true, string.Empty, key, 0);
    }

    public static CastOutcome Fail(string reason)
    {
        return new CastOutcome(false, reason, null, 0);
    }

    public static CastOutcome Fizzle(string key, int hpLost)
    {
        return new CastOutcome(false, "Fizzle", key, hpLost);
    }
}