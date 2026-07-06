using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

public class CampaignCreationEvals
{
    private static readonly string[] CreateCampaignTools =
        ["CreateCharacter", "ConfigureCampaign"];

    [Fact]
    public async Task Turn1_Begin_CallsNoTools()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn1-Begin");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("begin");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext([])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected no tools on 'begin'; got [{string.Join(", ", outcome.ToolCalls)}]");
    }

    [Fact]
    public async Task Turn2_Name_CreatesCampaignInOrder()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn2-Name");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);

        // Turn 1 first so the model has asked for a name and history is consistent.
        await host.CreateTurnRunner().RunTurnAsync("begin");
        var outcome = await host.CreateTurnRunner().RunTurnAsync("Grim");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");
    }

    [Fact]
    public async Task Combat_InventoryQuestion_AnswersWithoutTakingTurn()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Combat-InventoryQuestion-NoTurn");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateCombatAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("What do I have in my inventory and equipment?");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext([])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected no combat tools for an inventory question; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.Contains("staff", outcome.Narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Combat_MissingItemUse_DoesNotInventItemOrTakeTurn()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Combat-MissingItemUse-NoTurn");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateCombatAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I light my lantern and throw it at the priest.");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext([])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected no combat tools for a missing lantern; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.True(
            MentionsMissingItem(outcome.Narrative, "lantern"),
            $"Expected the GM to say the lantern is unavailable. Narrative: {outcome.Narrative}");
    }

    private static bool MentionsMissingItem(string narrative, string item)
    {
        var text = narrative.ToLowerInvariant();
        return text.Contains(item, StringComparison.Ordinal)
               && (text.Contains("do not have", StringComparison.Ordinal)
                   || text.Contains("don't have", StringComparison.Ordinal)
                   || text.Contains("not have", StringComparison.Ordinal)
                   || text.Contains("no lantern", StringComparison.Ordinal)
                   || text.Contains("not in", StringComparison.Ordinal)
                   || text.Contains("absent", StringComparison.Ordinal)
                   || text.Contains("missing", StringComparison.Ordinal));
    }
}
