using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Carries the tool names a scenario expects into <see cref="ToolCallEvaluator"/>. How they are
/// compared — exact ordered sequence or order-insensitive containment — is the evaluator's mode, not
/// the context's, so a scenario states the expectation once. The base name/content are for
/// human-readable reporting.
/// </summary>
public sealed class ToolCallsContext(IReadOnlyList<string> tools)
    : EvaluationContext(
        name: ContextName,
        content: tools.Count == 0 ? "(no tools)" : string.Join(", ", tools))
{
    public const string ContextName = "Expected Tool Calls";

    public IReadOnlyList<string> Tools { get; } = tools;
}

/// <summary>
/// Deterministic, non-AI evaluator over the tool calls in a <see cref="ChatResponse"/>, in one of two
/// modes. Ordered: the calls must match the expected sequence EXACTLY (same tools, same order, no
/// extras). Unordered: every expected tool must appear somewhere, extras allowed. Expectations come
/// from a <see cref="ToolCallsContext"/> in <c>additionalContext</c>. The metric name reflects the
/// mode, so ordered and containment checks stay separate series in the report.
/// </summary>
public sealed class ToolCallEvaluator(bool ordered) : IEvaluator
{
    public const string OrderedMetricName = "Tool Call Order";
    public const string ContainsMetricName = "Tool Call Contains";

    private string MetricName => ordered ? OrderedMetricName : ContainsMetricName;

    public IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (additionalContext?.OfType<ToolCallsContext>().FirstOrDefault() is not { } context)
        {
            // No context provided — report an error metric with Value = null (indeterminate)
            var errMetric = new BooleanMetric(MetricName, null, null);
            errMetric.Diagnostics = [EvaluationDiagnostic.Error(
                $"No {nameof(ToolCallsContext)} was supplied in {nameof(additionalContext)}.")];
            return new ValueTask<EvaluationResult>(new EvaluationResult(errMetric));
        }

        List<string> actual = modelResponse.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .Select(c => c.Name)
            .ToList();

        bool passed = ordered
            ? actual.SequenceEqual(context.Tools, StringComparer.Ordinal)
            : context.Tools.All(t => actual.Contains(t, StringComparer.Ordinal));

        var metric = new BooleanMetric(MetricName, passed, null);
        metric.Diagnostics = [EvaluationDiagnostic.Informational(
            $"expected ({(ordered ? "ordered" : "contains")}): [{string.Join(", ", context.Tools)}]; "
            + $"actual: [{string.Join(", ", actual)}]")];

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
