#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Models;

namespace WretchedWhispers.Api.Services;

public interface IAgentExecutor
{
    IAsyncEnumerable<GameTurnEvent> ExecuteAsync(
        Kernel kernel,
        SessionContext sessionContext,
        Guid chatSessionId,
        string playerMessage,
        CancellationToken ct);
}
