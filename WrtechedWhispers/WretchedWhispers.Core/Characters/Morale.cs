namespace WretchedWhispers.Core.Characters;

public static class Morale
{
    public enum Result
    {
        Holds,
        Flees,
        Surrenders
    }

    /// <summary>
    ///     Roll 2d6; if strictly greater than the creature's Morale, it's demoralized.
    ///     Then roll d6: 1-3 flee, 4-6 surrender.
    /// </summary>
    public static Result Check(IRandomService rng, int morale)
    {
        var twoD6 = rng.D(2, 6);
        if (twoD6 > morale)
        {
            var d6 = rng.D(6);
            return d6 <= 3 ? Result.Flees : Result.Surrenders;
        }

        return Result.Holds;
    }
}