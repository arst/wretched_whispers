namespace WretchedWhispers.Core.Combat;

public static class Reaction
{
    public enum Disposition
    {
        Kill,
        Angered,
        Indifferent,
        AlmostFriendly,
        Helpful
    }

    public static Disposition Roll(IRandomService rng)
    {
        var total = rng.D(2, 6); // 2d6
        return total switch
        {
            <= 3 => Disposition.Kill,
            <= 6 => Disposition.Angered,
            <= 8 => Disposition.Indifferent,
            <= 10 => Disposition.AlmostFriendly,
            _ => Disposition.Helpful
        };
    }
}