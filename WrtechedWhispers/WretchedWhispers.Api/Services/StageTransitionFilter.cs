using Microsoft.SemanticKernel;

#pragma warning disable SKEXP0001

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Enforces stage boundaries during a turn. The stage is locked at construction time
/// (turn start) and never re-derived — functions outside the initial stage's allowed
/// set are blocked for the entire turn, preventing runaway multi-stage chains.
/// </summary>
public sealed class StageTransitionFilter : IAutoFunctionInvocationFilter
{
    private readonly HashSet<(string? Plugin, string Function)> _allowedFunctions;
    private readonly SessionStage _lockedStage;

    public StageTransitionFilter(
        SessionStage lockedStage,
        IReadOnlyList<KernelFunction> allowedFunctions)
    {
        _lockedStage = lockedStage;
        _allowedFunctions = allowedFunctions
            .Select(f => (f.PluginName, f.Name))
            .ToHashSet();
    }

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        var pluginName = context.Function.PluginName;
        var functionName = context.Function.Name;

        if (!_allowedFunctions.Contains((pluginName, functionName)))
        {
            // Block the call — return a corrective error to steer the model
            context.Result = new FunctionResult(
                context.Function,
                $"[BLOCKED] {pluginName}.{functionName} is not available in the {_lockedStage} stage. " +
                "Focus on the current stage's task and respond to the player.");

            // Terminate the auto function invocation loop
            context.Terminate = true;
            return;
        }

        await next(context);
    }
}
