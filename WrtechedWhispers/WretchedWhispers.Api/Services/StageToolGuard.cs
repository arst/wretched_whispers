using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Defense-in-depth stage guardrail, implemented as Microsoft Agent Framework function-invocation
/// middleware. The primary guardrail is that <see cref="AgentToolProvider"/> builds each turn's agent
/// with ONLY the stage-legal tools, so an out-of-stage call should be impossible. This middleware
/// independently re-checks every actual tool invocation against the authoritative
/// <see cref="GameToolCatalog"/> for the stage and blocks anything that slipped through — catching a
/// regression in tool construction at the moment of invocation rather than letting it mutate state.
/// </summary>
public static class StageToolGuard
{
    public static AIAgent WithStageToolGuard(this AIAgent agent, SessionStage stage, ILogger logger)
    {
        var legalTools = GameToolCatalog.ForStage(stage)
            .Select(d => d.Name)
            .ToHashSet(StringComparer.Ordinal);

        return agent
            .AsBuilder()
            .Use(async (AIAgent _, FunctionInvocationContext context,
                Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct) =>
            {
                var toolName = context.Function.Name;
                if (!legalTools.Contains(toolName))
                {
                    logger.LogError(
                        "Blocked out-of-stage tool call: {Tool} is not permitted in stage {Stage}",
                        toolName, stage);
                    throw new InvalidOperationException(
                        $"Tool '{toolName}' is not permitted in the {stage} stage.");
                }

                return await next(context, ct);
            })
            .Build();
    }
}
