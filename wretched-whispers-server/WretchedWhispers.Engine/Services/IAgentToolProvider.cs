using Microsoft.Extensions.AI;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Builds the tools (<see cref="AIFunction"/>) the game-master agent is given for a turn: every game tool,
/// each refusing a call outside the stage it belongs to (see <see cref="GameToolCatalog"/>).
/// </summary>
public interface IAgentToolProvider
{
    (IReadOnlyList<AIFunction> Tools, string[] RegisteredFunctions) GetTools(SessionContext sessionContext);
}
