using System.ComponentModel;
using WretchedWhispers.Api.GameTools;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Thin wrapper around DicePlugin for consistent plugin naming.
/// No ID filling needed -- dice rolls don't require entity context.
/// </summary>
[Description("Roll dice expressions for game mechanics.")]
public sealed class DiceWrapperPlugin(IDiceOperations inner)
{
    [Description("Roll a dice expression, e.g. 'd6', '1d8', '1d6+2', '2d10-3'")]
    public DiceRollResult Roll(
        [Description("Dice expression to roll (e.g., 'd6', '1d8', '1d6+2', '2d10-3')")] string diceExpression)
    {
        ToolGuard.DiceExpression(diceExpression, nameof(diceExpression));
        return inner.Roll(diceExpression);
    }
}
