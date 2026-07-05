using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace WretchedWhispers.Evals.Evaluators;

public class ToolCallContainsEvaluatorTests
{
    private static ChatResponse ResponseWithCalls(params string[] names)
    {
        var contents = names
            .Select((n, i) => (AIContent)new FunctionCallContent($"call_{i}", n))
            .ToList();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static async Task<BooleanMetric> EvaluateAsync(ChatResponse response, string[] required)
    {
        var result = await new ToolCallContainsEvaluator().EvaluateAsync(
            messages: [],
            modelResponse: response,
            additionalContext: [new RequiredToolCallsContext(required)]);

        return result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
    }

    [Fact]
    public async Task Passes_WhenRequiredCallPresent_AmongOthers()
    {
        var response = ResponseWithCalls("ChallengeCharacter", "RecordJournalEntry");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry"]);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Fails_WhenRequiredCallMissing()
    {
        var response = ResponseWithCalls("ChallengeCharacter");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry"]);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task Passes_WhenOrderDiffers()
    {
        var response = ResponseWithCalls("RecordJournalEntry", "ResolveCombatRound");
        var metric = await EvaluateAsync(response, ["ResolveCombatRound", "RecordJournalEntry"]);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Passes_WhenNoToolsRequired()
    {
        var response = ResponseWithCalls("ChallengeCharacter");
        var metric = await EvaluateAsync(response, []);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Fails_WhenSomeRequiredCallsMissing()
    {
        var response = ResponseWithCalls("RecordJournalEntry");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry", "ResolveCombatRound"]);
        Assert.False(metric.Value);
    }
}
