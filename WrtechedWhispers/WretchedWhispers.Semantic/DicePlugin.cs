using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Semantic;

[Description("Plugin to roll dice expressions.")]
public class DicePlugin(IRandomService diceService)
{
    [KernelFunction]
    [Description("Roll a dice expression, e.g. 'd6', '1d8', '1d6+2', '2d10-3' and so on.")]
    public int Roll(
        [Description("Dice expression to roll (e.g., 'd6', '1d8', '1d6+2', '2d10-3')")] string dexExpression)
    {
        var dex = DiceExpr.Parse(dexExpression);
        var dice = new Dice(diceService);

        return dice.Roll(dex);
    }
}

