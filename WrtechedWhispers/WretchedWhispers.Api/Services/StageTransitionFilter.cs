using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001

namespace WretchedWhispers.Api.Services;

public sealed class StageTransitionFilter(SessionContext sessionContext) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        // Execute the function first
        await next(context);

        // Check if this function call should trigger a stage transition
        var currentStage = sessionContext.DeriveStage();
        var pluginName = context.Function.PluginName;
        if (pluginName is null)
            return;

        var nextStage = StageTransitions.GetNextStage(
            currentStage,
            pluginName,
            context.Function.Name);

        // Stage transitions are handled by re-deriving from domain state on next turn.
        // The transition map validates that the function call is a valid transition trigger.
        // Domain state mutation (by the plugin) is what actually changes the derived stage.
        // The filter's main role is for logging/telemetry and potential future guardrail logic.
    }
}
