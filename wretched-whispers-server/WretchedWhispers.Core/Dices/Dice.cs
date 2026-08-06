namespace WretchedWhispers.Core.Dices;

public sealed class Dice(IRandomService randomService)
{
    private readonly IRandomService _randomService =
        randomService ?? throw new ArgumentNullException(nameof(randomService));

    public int Roll(DiceExpr expr)
    {
        return D(expr.Count, expr.Sides) + expr.Constant;
    }

    private int D(int sides)
    {
        return 1 + _randomService.GenerateRandomRoll(sides);
    }

    private int D(int count, int sides)
    {
        var sum = 0;
        for (var i = 0; i < count; i++) sum += D(sides);
        return sum;
    }
}
