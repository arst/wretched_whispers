using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Plugins.CombatAgent;

public interface ICombatAgentService
{
    IAsyncEnumerable<GameTurnEvent> ResolveCombatAsync(
        SessionContext sessionContext,
        IReadOnlyList<AIFunction> tools,
        CancellationToken ct);
}
