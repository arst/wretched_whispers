using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins.Adapters;

/// <summary>
/// Adapts DicePlugin to IDiceOperations.
/// </summary>
public sealed class DicePluginAdapter(DicePlugin inner) : IDiceOperations
{
    public DiceRollResult Roll(string diceExpression) => inner.Roll(diceExpression);
}
