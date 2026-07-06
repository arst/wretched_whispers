using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Carries the required (order-insensitive) tool-call names into <see cref="ToolCallContainsEvaluator"/>
/// for a single scenario run. The strongly-typed <see cref="Required"/> array is what the evaluator
/// reads; the base name/content are for human-readable reporting.
/// </summary>
public sealed class RequiredToolCallsContext(string[] required)
    : EvaluationContext(
        name: ContextName,
        content: required.Length == 0 ? "(no tools)" : string.Join(", ", required))
{
    public const string ContextName = "Required Tool Calls";

    public string[] Required { get; } = required;
}
