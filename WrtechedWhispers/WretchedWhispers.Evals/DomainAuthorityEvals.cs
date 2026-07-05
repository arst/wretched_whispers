using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

public class DomainAuthorityEvals
{
    private static ReportingConfiguration CreateReporting(IChatClient chatClient) =>
        EvalSupport.CreateReportingConfiguration(
            chatClient,
            [new ToolCallOrderEvaluator(), new ToolCallContainsEvaluator()],
            "domain-authority");

    [Fact]
    public async Task Combat_PlayerAttack_ResolvesExactlyOneRound()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReporting(chatClient);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Combat-PlayerAttack-OneRound");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateCombatAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(["ResolveCombatRound"])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected exactly one ResolveCombatRound; got [{string.Join(", ", outcome.ToolCalls)}]");
    }

    [Fact]
    public async Task Exploration_MemorableNpc_GetsJournaled()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReporting(chatClient);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Exploration-MemorableNpc-Journaled");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateExplorationAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I approach the gallows-keeper, ask his name, and swear I'll bring him the hangman's rope by dawn.");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new RequiredToolCallsContext(["RecordJournalEntry"])]);

        var metric = result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected a RecordJournalEntry call; got [{string.Join(", ", outcome.ToolCalls)}]");
    }
}
