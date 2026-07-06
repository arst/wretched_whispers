using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Deterministic, non-AI evaluator: checks that every required tool name (from a
/// <see cref="RequiredToolCallsContext"/> in <c>additionalContext</c>) appears among the actual tool
/// calls in a <see cref="ChatResponse"/> — order-insensitive, extra calls allowed.
/// </summary>
public sealed class ToolCallContainsEvaluator : IEvaluator
{
    public const string MetricName = "Tool Call Contains";

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (additionalContext?.OfType<RequiredToolCallsContext>().FirstOrDefault() is not { } context)
        {
            // No context provided — report an error metric with Value = null (indeterminate)
            var errMetric = new BooleanMetric(MetricName, null, null);
            errMetric.Diagnostics = [EvaluationDiagnostic.Error(
                $"No {nameof(RequiredToolCallsContext)} was supplied in {nameof(additionalContext)}.")];
            return new ValueTask<EvaluationResult>(new EvaluationResult(errMetric));
        }

        List<string> actual = modelResponse.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .Select(c => c.Name)
            .ToList();

        bool passed = context.Required.All(r => actual.Contains(r, StringComparer.Ordinal));

        var metric = new BooleanMetric(MetricName, passed, null);
        metric.Diagnostics = [EvaluationDiagnostic.Informational(
            $"required: [{string.Join(", ", context.Required)}]; actual: [{string.Join(", ", actual)}]")];

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
