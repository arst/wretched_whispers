using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;

namespace WretchedWhispers.Api.Services;

public interface IAgentExecutor
{
    IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        IReadOnlyList<AIFunction> tools,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        CancellationToken ct);
}
