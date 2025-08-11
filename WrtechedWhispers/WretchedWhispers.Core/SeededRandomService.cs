using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core;

public sealed class SeededRandomService : IRandomService
{
    private readonly Random _rng;

    public SeededRandomService(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public int D(int sides)
    {
        return 1 + _rng.Next(sides);
    }

    public int D(int count, int sides)
    {
        var sum = 0;
        for (var i = 0; i < count; i++) sum += D(sides);
        return sum;
    }

    public int Roll(DiceExpr expr)
    {
        return D(expr.Count, expr.Sides) + expr.Constant;
    }
}