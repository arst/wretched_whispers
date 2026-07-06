using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
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

    // GroundednessEvaluator hardcodes Temperature = 0 on its judge request. Reasoning-model
    // deployments (e.g. gpt-5-mini) reject any non-default temperature, so strip it back to null
    // (provider default). NOTE: this wraps the scenario's ONE shared client, so it applies to the
    // game-under-test turn as well as the judge call — a no-op there today (AgentExecutor never
    // sets Temperature), but rescope if a future scenario's game path relies on a temperature.
    private static ReportingConfiguration CreateGroundednessReporting(IChatClient chatClient) =>
        EvalSupport.CreateReportingConfiguration(
            chatClient.AsBuilder().ConfigureOptions(o => o.Temperature = null).Build(),
            [new GroundednessEvaluator()],
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
            additionalContext: [new RequiredToolCallsContext(["ResolveCombatRound"])]);

        var metric = result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
        Assert.True(metric.Value, $"Expected a ResolveCombatRound call; got [{string.Join(", ", outcome.ToolCalls)}]");

        // The persona instructs the GM to journal notable events, and a combat kill qualifies —
        // so RecordJournalEntry is permitted alongside the round. Everything else stays strict:
        // exactly one ResolveCombatRound (one player action = one round), no other tool may appear.
        Assert.Equal(1, outcome.ToolCalls.Count(c => c == "ResolveCombatRound"));
        Assert.All(
            outcome.ToolCalls.Where(c => c != "ResolveCombatRound"),
            c => Assert.Equal("RecordJournalEntry", c));
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

    [Fact]
    public async Task Exploration_BuyingItem_CallsBuyItem()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReporting(chatClient);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Exploration-BuyItem-DeductsAndAdds");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateExplorationAsync(chatConfiguration.ChatClient);
        // A purchase must go through BuyItem (deduct silver + add item). Regression guard against the GM
        // narrating the transaction — silver spent, map gained — while only rolling a haggle check and
        // journaling, leaving inventory and silver untouched.
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I haggle with the market crone and buy the tattered map fragment from her for 4 silver.");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new RequiredToolCallsContext(["BuyItem"])]);

        var metric = result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected a BuyItem call for a purchase; got [{string.Join(", ", outcome.ToolCalls)}]. " +
            "The GM must not narrate silver spent or an item gained without BuyItem.");
    }

    [Fact]
    public async Task Combat_Narration_IsGroundedInToolResults()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateGroundednessReporting(chatClient);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("Combat-Narration-Grounded");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateCombatAsync(chatConfiguration.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!");

        var groundingContext = string.Join("\n",
            outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

        EvaluationResult result = await run.EvaluateAsync(
            messages: [new ChatMessage(ChatRole.User, "I strike the plague priest with my staff!")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)]);

        var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
        Assert.NotNull(metric.Value);
        Assert.True(metric.Value >= 4, $"Groundedness {metric.Value}: narration drifted from tool results. Narrative: {outcome.Narrative}");
    }
}
