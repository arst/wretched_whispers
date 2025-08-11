namespace WretchedWhispers.Core.Dice;

public class Dice(IRandomService rng)
{
    public int Roll(DiceExpr expr)
    {
        return rng.Roll(expr);
    }
}