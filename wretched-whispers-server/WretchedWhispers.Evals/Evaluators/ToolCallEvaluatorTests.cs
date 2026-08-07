using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace WretchedWhispers.Evals.Evaluators;

public class ToolCallEvaluatorTests
{
    private static ChatResponse ResponseWithCalls(params string[] names)
    {
        var contents = names
            .Select((n, i) => (AIContent)new FunctionCallContent($"call_{i}", n))
            .ToList();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static async Task<BooleanMetric> EvaluateAsync(
        ChatResponse response, IReadOnlyList<string> expected, bool ordered)
    {
        var evaluator = new ToolCallEvaluator(ordered);
        var result = await evaluator.EvaluateAsync(
            messages: [],
            modelResponse: response,
            additionalContext: [new ToolCallsContext(expected)]);

        return result.Get<BooleanMetric>(
            ordered ? ToolCallEvaluator.OrderedMetricName : ToolCallEvaluator.ContainsMetricName);
    }

    // --- ordered mode ---

    [Fact]
    public async Task Ordered_ExactMatch_Passes()
    {
        var response = ResponseWithCalls("CreateEncounter", "AddAdversaryToEncounter", "StartEncounter");
        var metric = await EvaluateAsync(response, ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"], ordered: true);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Ordered_WrongOrder_Fails()
    {
        var response = ResponseWithCalls("AddAdversaryToEncounter", "CreateEncounter", "StartEncounter");
        var metric = await EvaluateAsync(response, ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"], ordered: true);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task Ordered_MissingTool_Fails()
    {
        var response = ResponseWithCalls("CreateEncounter", "AddAdversaryToEncounter");
        var metric = await EvaluateAsync(response, ["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"], ordered: true);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task Ordered_NoToolsExpected_NoneCalled_Passes()
    {
        var metric = await EvaluateAsync(ResponseWithCalls(), [], ordered: true);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Ordered_NoToolsExpected_ButOneCalled_Fails()
    {
        var metric = await EvaluateAsync(ResponseWithCalls("ChallengeCharacter"), [], ordered: true);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task Ordered_MultiMessage_WithText_ReadsToolCallsInOrder_IgnoringText()
    {
        var response = new ChatResponse(new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("Working on it..."),
                new FunctionCallContent("call_0", "CreateEncounter")
            }),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "AddAdversaryToEncounter"),
                new TextContent("done")
            })
        });

        var metric = await EvaluateAsync(response, ["CreateEncounter", "AddAdversaryToEncounter"], ordered: true);
        Assert.True(metric.Value);
    }

    // --- contains mode ---

    [Fact]
    public async Task Contains_RequiredCallPresent_AmongOthers_Passes()
    {
        var response = ResponseWithCalls("ChallengeCharacter", "RecordJournalEntry");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry"], ordered: false);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Contains_RequiredCallMissing_Fails()
    {
        var response = ResponseWithCalls("ChallengeCharacter");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry"], ordered: false);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task Contains_OrderDiffers_Passes()
    {
        var response = ResponseWithCalls("RecordJournalEntry", "ResolveCombatRound");
        var metric = await EvaluateAsync(response, ["ResolveCombatRound", "RecordJournalEntry"], ordered: false);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Contains_NoToolsRequired_Passes()
    {
        var response = ResponseWithCalls("ChallengeCharacter");
        var metric = await EvaluateAsync(response, [], ordered: false);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task Contains_SomeRequiredCallsMissing_Fails()
    {
        var response = ResponseWithCalls("RecordJournalEntry");
        var metric = await EvaluateAsync(response, ["RecordJournalEntry", "ResolveCombatRound"], ordered: false);
        Assert.False(metric.Value);
    }

    // --- shared ---

    [Fact]
    public async Task MissingContext_ReportsIndeterminate()
    {
        var result = await new ToolCallEvaluator(ordered: true).EvaluateAsync(
            messages: [], modelResponse: ResponseWithCalls("ChallengeCharacter"));
        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.OrderedMetricName);
        Assert.Null(metric.Value);
    }
}
