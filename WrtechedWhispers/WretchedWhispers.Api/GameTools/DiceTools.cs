using System.ComponentModel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Api.GameTools;

[Description("Result of a dice roll with the formula used and numeric outcome.")]
public record DiceRollResult(
    [property: Description("The dice expression that was rolled (e.g., 'd20', '1d6+2')")]
    string Formula,
    [property: Description("The numeric result of the roll")]
    int Result);

/// <summary>
/// Dice game-master tool. Validates the expression before parsing so a malformed model argument is
/// rejected with a readable message rather than throwing deep in the parser. Replaces the former
/// DiceWrapperPlugin → IDiceOperations → DicePluginAdapter → DicePlugin stack.
/// </summary>
[Description("Roll dice expressions for game mechanics.")]
public sealed class DiceTools(Dice dice)
{
    [Description("Roll a dice expression, e.g. 'd6', '1d8', '1d6+2', '2d10-3'")]
    [GameTool(SessionStage.Exploration, SessionStage.Combat)]
    public DiceRollResult Roll(
        [Description("Dice expression to roll (e.g., 'd6', '1d8', '1d6+2', '2d10-3')")] string diceExpression)
    {
        ToolGuard.DiceExpression(diceExpression, nameof(diceExpression));
        return new DiceRollResult(diceExpression, dice.Roll(DiceExpr.Parse(diceExpression)));
    }
}
