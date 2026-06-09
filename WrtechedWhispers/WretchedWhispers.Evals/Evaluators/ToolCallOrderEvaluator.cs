using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Deterministic, non-AI evaluator: checks that the tool calls in a <see cref="ChatResponse"/> match an
/// expected ordered sequence EXACTLY (same tools, same order, no extras). Expected order is supplied via
/// an <see cref="ExpectedToolCallOrderContext"/> in <c>additionalContext</c>; actual order is read from
/// the response's <see cref="FunctionCallContent"/>s.
/// </summary>
public sealed class ToolCallOrderEvaluator : IEvaluator
{
    public const string MetricName = "Tool Call Order";

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (additionalContext?.OfType<ExpectedToolCallOrderContext>().FirstOrDefault() is not { } context)
        {
            // No context provided — report an error metric with Value = null (indeterminate)
            var errMetric = new BooleanMetric(MetricName, null, null);
            errMetric.Diagnostics = [EvaluationDiagnostic.Error(
                $"No {nameof(ExpectedToolCallOrderContext)} was supplied in {nameof(additionalContext)}.")];
            return new ValueTask<EvaluationResult>(new EvaluationResult(errMetric));
        }

        List<string> actual = modelResponse.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .Select(c => c.Name)
            .ToList();

        bool passed = actual.SequenceEqual(context.Expected, StringComparer.Ordinal);

        var metric = new BooleanMetric(MetricName, passed, null);
        metric.Diagnostics = [EvaluationDiagnostic.Informational(
            $"expected: [{string.Join(", ", context.Expected)}]; actual: [{string.Join(", ", actual)}]")];

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
