using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Carries the expected ordered tool-call names into <see cref="ToolCallOrderEvaluator"/> for a single
/// scenario run. The strongly-typed <see cref="Expected"/> list is what the evaluator reads; the base
/// name/content are for human-readable reporting.
/// </summary>
public sealed class ExpectedToolCallOrderContext : EvaluationContext
{
    public IReadOnlyList<string> Expected { get; }

    // EvaluationContext base ctor: protected(string name, string content)
    public ExpectedToolCallOrderContext(IReadOnlyList<string> expected)
        : base(
            name: "Expected Tool Call Order",
            content: expected.Count == 0 ? "(no tools)" : string.Join(" -> ", expected))
    {
        Expected = expected;
    }
}
