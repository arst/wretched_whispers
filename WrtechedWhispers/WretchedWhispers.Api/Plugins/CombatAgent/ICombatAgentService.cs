#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Plugins.CombatAgent;

public interface ICombatAgentService
{
    IAsyncEnumerable<GameTurnEvent> ResolveCombatAsync(
        SessionContext sessionContext,
        Kernel gameKernel,
        CancellationToken ct);
}
