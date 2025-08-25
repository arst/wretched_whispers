namespace WretchedWhispers.Core.Dices;

public static class Dice
{
    private static IRandomService _randomService = new SeededRandomService();

    public static void SetRandomGenerator(IRandomService randomService)
    {
        _randomService = randomService;
    }

    private static int D(int sides)
    {
        return 1 + _randomService.GenerateRandomRoll(sides);
    }

    private static int D(int count, int sides)
    {
        var sum = 0;
        for (var i = 0; i < count; i++) sum += D(sides);
        return sum;
    }

    public static int Roll(DiceExpr expr)
    {
        return D(expr.Count, expr.Sides) + expr.Constant;
    }
}