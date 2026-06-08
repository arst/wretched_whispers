using WretchedWhispers.Api.GameTools;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Contract for dice operations that wrapper plugins delegate to.
/// </summary>
public interface IDiceOperations
{
    DiceRollResult Roll(string diceExpression);
}
