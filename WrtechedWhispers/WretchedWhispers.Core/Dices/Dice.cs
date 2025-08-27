namespace WretchedWhispers.Core.Dices;

public static class Dice
{
    private static IRandomService? _randomService;

    public static void SetRandomGenerator(IRandomService randomService)
    {
        _randomService = randomService;
    }

    public static int Roll(DiceExpr expr)
    {
        return D(expr.Count, expr.Sides) + expr.Constant;
    }

    private static int D(int sides)
    {
        CheckRandomServiceInitialization();
        return 1 + _randomService!.GenerateRandomRoll(sides);
    }

    private static void CheckRandomServiceInitialization()
    {
        if (_randomService is null)
            throw new InvalidOperationException("Dice must be initialized with a random service before use.");
    }

    private static int D(int count, int sides)
    {
        var sum = 0;
        for (var i = 0; i < count; i++) sum += D(sides);
        return sum;
    }
}