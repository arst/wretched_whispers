using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Semantic;

[Description("Result of a dice roll with the formula used and numeric outcome.")]
public record DiceRollResult(
    [property: Description("The dice expression that was rolled (e.g., 'd20', '1d6+2')")]
    string Formula,
    [property: Description("The numeric result of the roll")]
    int Result);

[Description("Plugin to roll dice expressions.")]
public class DicePlugin(Dice dice)
{
    [KernelFunction]
    [Description("Roll a dice expression, e.g. 'd6', '1d8', '1d6+2', '2d10-3' and so on.")]
    public DiceRollResult Roll(
        [Description("Dice expression to roll (e.g., 'd6', '1d8', '1d6+2', '2d10-3')")]
        string dexExpression)
    {
        var dex = DiceExpr.Parse(dexExpression);
        return new DiceRollResult(dexExpression, dice.Roll(dex));
    }
}
