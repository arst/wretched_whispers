using Microsoft.Extensions.AI;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Builds the set of tools (<see cref="AIFunction"/>) the game-master agent is allowed to call
/// for a given <see cref="SessionStage"/>. The agent for a turn is constructed with ONLY these
/// tools, so out-of-stage actions are physically impossible (see <see cref="GameToolCatalog"/>).
/// </summary>
public interface IAgentToolProvider
{
    (IReadOnlyList<AIFunction> Tools, string[] RegisteredFunctions) GetToolsForStage(
        SessionContext sessionContext, SessionStage stage);
}
