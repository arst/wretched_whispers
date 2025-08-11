using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core;

public interface IRandomService
{
    /// <summary>Return an integer in [1, sides].</summary>
    int D(int sides);

    /// <summary>Roll multiple dice: sum of count * d(sides)</summary>
    int D(int count, int sides);

    /// <summary>Roll NdM + K using a concise helper.</summary>
    int Roll(DiceExpr expr);
}