using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace WretchedWhispers.Evals.Evaluators;

public class ToolCallOrderEvaluatorTests
{
    private static ChatResponse ResponseWithToolCalls(params string[] names)
    {
        var contents = names
            .Select((n, i) => (AIContent)new FunctionCallContent($"call_{i}", n))
            .ToList();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static async Task<BooleanMetric> EvaluateAsync(ChatResponse response, IReadOnlyList<string> expected)
    {
        var evaluator = new ToolCallOrderEvaluator();
        var context = new ExpectedToolCallOrderContext(expected);
        EvaluationResult result = await evaluator.EvaluateAsync(
            messages: [],
            modelResponse: response,
            additionalContext: [context]);

        return result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
    }

    [Fact]
    public async Task ExactOrderMatch_Passes()
    {
        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign", "StartCampaign");
        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task WrongOrder_Fails()
    {
        var response = ResponseWithToolCalls("ConfigureCampaign", "CreateCharacter", "StartCampaign");
        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task MissingTool_Fails()
    {
        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign");
        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task NoToolsExpected_NoneCalled_Passes()
    {
        var response = ResponseWithToolCalls(); // none
        var metric = await EvaluateAsync(response, []);
        Assert.True(metric.Value);
    }

    [Fact]
    public async Task NoToolsExpected_ButOneCalled_Fails()
    {
        var response = ResponseWithToolCalls("CreateCharacter");
        var metric = await EvaluateAsync(response, []);
        Assert.False(metric.Value);
    }

    [Fact]
    public async Task MultiMessage_WithText_ReadsToolCallsInOrder_IgnoringText()
    {
        var response = new ChatResponse(new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("Working on it..."),
                new FunctionCallContent("call_0", "CreateCharacter")
            }),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call_1", "ConfigureCampaign"),
                new TextContent("done")
            })
        });

        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign"]);
        Assert.True(metric.Value);
    }
}
