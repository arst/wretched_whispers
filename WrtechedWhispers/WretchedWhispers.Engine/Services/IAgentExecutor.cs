using Microsoft.Extensions.AI;
using WretchedWhispers.Engine.Models;

namespace WretchedWhispers.Engine.Services;

public interface IAgentExecutor
{
    IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        IReadOnlyList<AIFunction> tools,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        CancellationToken ct);
}
