using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001

namespace WretchedWhispers.Api.Services;

public sealed class StageTransitionFilter(
    SessionContext sessionContext,
    StagePluginRegistry stagePluginRegistry,
    Kernel kernel) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        // Derive stage from current domain state (reflects mutations from earlier calls in this turn)
        var currentStage = sessionContext.DeriveStage();
        var pluginName = context.Function.PluginName;
        var functionName = context.Function.Name;

        // Check if this function is allowed in the current stage
        var allowedFunctions = stagePluginRegistry.GetFunctionsForStage(currentStage, kernel);
        var isAllowed = allowedFunctions.Any(f =>
            f.PluginName == pluginName && f.Name == functionName);

        if (!isAllowed)
        {
            // Block the call — return a corrective error to steer the model
            context.Result = new FunctionResult(
                context.Function,
                $"[BLOCKED] {pluginName}.{functionName} is not available in the {currentStage} stage. " +
                $"Available functions: {string.Join(", ", allowedFunctions.Select(f => f.Name))}. " +
                "Please use only the available functions for the current stage.");

            // Terminate the auto function invocation loop to prevent further out-of-stage calls
            context.Terminate = true;
            return;
        }

        // Function is allowed — execute it
        await next(context);
    }
}
