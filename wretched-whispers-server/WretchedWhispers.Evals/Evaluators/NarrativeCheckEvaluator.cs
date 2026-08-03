using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace WretchedWhispers.Evals.Evaluators;

/// <summary>
/// Carries the plain-language criterion the narration must satisfy into
/// <see cref="NarrativeCheckEvaluator"/> for a single scenario run.
/// </summary>
public sealed class NarrativeCheckContext(string criterion)
    : EvaluationContext(name: ContextName, content: criterion)
{
    public const string ContextName = "Narrative Check Criterion";

    public string Criterion { get; } = criterion;
}

/// <summary>
/// LLM-judge boolean evaluator: asks the judge model whether the narration satisfies a
/// plain-language criterion. Replaces hand-enumerated keyword probes, which fail on any valid
/// paraphrase ("your pack holds no such light" passes the narration but fails a "no lantern" probe).
/// Judge calls go through the scenario's caching client, so re-runs are free and deterministic.
/// </summary>
public sealed class NarrativeCheckEvaluator : IEvaluator
{
    public const string MetricName = "Narrative Check";

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (additionalContext?.OfType<NarrativeCheckContext>().FirstOrDefault() is not { } context)
            return Error($"No {nameof(NarrativeCheckContext)} was supplied in {nameof(additionalContext)}.");

        if (chatConfiguration is null)
            return Error($"{nameof(NarrativeCheckEvaluator)} is AI-based and requires a {nameof(ChatConfiguration)}.");

        var narrative = string.Concat(modelResponse.Messages
            .SelectMany(m => m.Contents ?? [])
            .OfType<TextContent>()
            .Select(t => t.Text));

        var prompt =
            "You are a strict test judge for a game master's narration.\n\n"
            + $"Criterion: {context.Criterion}\n\n"
            + "Narration:\n\"\"\"\n" + narrative + "\n\"\"\"\n\n"
            + "Does the narration satisfy the criterion? Reply with exactly one word: PASS or FAIL.";

        var response = await chatConfiguration.ChatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken);

        var verdict = response.Text.Trim();
        bool? passed = verdict.StartsWith("PASS", StringComparison.OrdinalIgnoreCase) ? true
            : verdict.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase) ? false
            : null;

        var metric = new BooleanMetric(MetricName, passed, null);
        metric.Diagnostics = [EvaluationDiagnostic.Informational(
            $"criterion: {context.Criterion}; judge verdict: {verdict}")];
        return new EvaluationResult(metric);
    }

    private static EvaluationResult Error(string message)
    {
        var metric = new BooleanMetric(MetricName, null, null);
        metric.Diagnostics = [EvaluationDiagnostic.Error(message)];
        return new EvaluationResult(metric);
    }
}
