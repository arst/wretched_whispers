using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

public class DomainAuthorityEvals
{
    private const string Suite = "domain-authority";

    /// <summary>The shared shape of every "this action MUST go through tool X" scenario: run one turn,
    /// assert the required tool was called. Returns the outcome for scenario-specific extra asserts.</summary>
    private static async Task<TurnOutcome> AssertToolRequiredAsync(
        string scenarioName,
        Func<IChatClient, Task<EvalHost>> createHost,
        string playerMessage,
        string requiredTool,
        string? failHint = null)
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, scenarioName, [new ToolCallContainsEvaluator()]);
        await using var host = await createHost(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(playerMessage);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new RequiredToolCallsContext([requiredTool])]);

        var metric = result.Get<BooleanMetric>(ToolCallContainsEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected a {requiredTool} call; got [{string.Join(", ", outcome.ToolCalls)}]."
            + (failHint is null ? "" : $" {failHint}"));
        return outcome;
    }

    [Fact]
    public async Task Combat_PlayerAttack_ResolvesExactlyOneRound()
    {
        var outcome = await AssertToolRequiredAsync(
            "Combat-PlayerAttack-OneRound",
            EvalHost.CreateCombatAsync,
            "I strike the plague priest with my staff!",
            "ResolveCombatRound");

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
        await AssertToolRequiredAsync(
            "Exploration-MemorableNpc-Journaled",
            client => EvalHost.CreateExplorationAsync(client),
            "I approach the gallows-keeper, ask his name, and swear I'll bring him the hangman's rope by dawn.",
            "RecordJournalEntry");
    }

    [Fact]
    public async Task Exploration_BuyingItem_CallsBuyItem()
    {
        // A purchase must go through BuyItem (deduct silver + add item). Regression guard against the GM
        // narrating the transaction — silver spent, map gained — while only rolling a haggle check and
        // journaling, leaving inventory and silver untouched.
        await AssertToolRequiredAsync(
            "Exploration-BuyItem-DeductsAndAdds",
            client => EvalHost.CreateExplorationAsync(client),
            "I haggle with the market crone and buy the tattered map fragment from her for 4 silver.",
            "BuyItem",
            "The GM must not narrate silver spent or an item gained without BuyItem.");
    }

    [Fact]
    public async Task Exploration_Resting_CallsRest()
    {
        // Resting must go through Rest (heals HP + restores abilities + advances time). Regression guard against
        // the GM narrating the character healing while only advancing time or journaling, leaving HP untouched.
        await AssertToolRequiredAsync(
            "Exploration-Rest-HealsViaRest",
            client => EvalHost.CreateExplorationAsync(client),
            "I find a sheltered corner of the ruin, bind my wounds, and rest for six hours to recover my strength.",
            "Rest",
            "The GM must not narrate healing or recovery without Rest.");
    }

    [Fact]
    public async Task Exploration_CastingScroll_CallsCastScroll()
    {
        // Casting a scroll must go through CastScroll (spends the use + returns the effect). Regression guard
        // against the GM narrating the spell going off while only rolling or journaling, leaving the scroll intact.
        await AssertToolRequiredAsync(
            "Exploration-CastScroll-SpendsUse",
            client => EvalHost.CreateExplorationAsync(client),
            "I unfurl my scroll and cast it, unleashing its power on the barred iron door before me.",
            "CastScroll",
            "The GM must not narrate a spell cast or a scroll spent without CastScroll.");
    }

    [Fact]
    public async Task Combat_InventoryQuestion_AnswersWithoutTakingTurn()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-InventoryQuestion-NoTurn", [new ToolCallOrderEvaluator()]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("What do I have in my inventory and equipment?");

        EvaluationResult result = await scenario.Run.EvaluateAsync(
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
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-MissingItemUse-NoTurn", [new ToolCallOrderEvaluator(), new NarrativeCheckEvaluator()]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I light my lantern and throw it at the priest.");

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext:
            [
                new ExpectedToolCallOrderContext([]),
                new NarrativeCheckContext(
                    "The player tried to use a lantern their character does not possess. The narration "
                    + "must make clear the lantern is not available (in any wording), and must NOT "
                    + "narrate the lantern actually being lit, thrown, or otherwise used."),
            ]);

        var orderMetric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(orderMetric.Value,
            $"Expected no combat tools for a missing lantern; got [{string.Join(", ", outcome.ToolCalls)}]");

        var narrativeMetric = result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName);
        Assert.True(narrativeMetric.Value,
            $"Expected the GM to refuse the missing lantern. Narrative: {outcome.Narrative}");
    }

    [Fact]
    public async Task Exploration_CampNarration_DoesNotFabricateItemUse()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Exploration-Camp-NoFabricatedItemUse", [new GroundednessEvaluator()], stripSamplingOptions: true);

        // Playtest regression: "camp for the night" produced "You take one of your torches...
        // Torches: 2 (you used one)" while the ONLY tool called was Rest — the GM invented the
        // consumption as camp color and computed its own count off the snapshot's x3.
        await using var host = await EvalHost.CreateExplorationAsync(
            scenario.ChatClient,
            [new InventoryItem(Guid.NewGuid(), "torches", isBulky: false, isOneTimeUse: true, quantity: 3)]);
        const string playerMessage = "We camp for the night. I sleep until dawn.";
        var outcome = await host.CreateTurnRunner().RunTurnAsync(playerMessage);

        var groundingContext =
            "Character inventory before the turn: torches x3. Inventory changes ONLY through a "
            + "UseItemFromCharacterInventory result below; without one, every count is unchanged.\n"
            + string.Join("\n", outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [new ChatMessage(ChatRole.User, playerMessage)],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)]);

        var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
        Assert.NotNull(metric.Value);
        Assert.True(metric.Value >= EvalSupport.GroundednessPassBar,
            $"Groundedness {metric.Value}: narration claimed item usage or counts no tool applied "
            + $"(UseItem called: {outcome.ToolCalls.Contains("UseItemFromCharacterInventory")}). "
            + $"Narrative: {outcome.Narrative}");
    }

    [Fact]
    public async Task Combat_Narration_IsGroundedInToolResults()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-Narration-Grounded", [new GroundednessEvaluator()], stripSamplingOptions: true);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!");

        var groundingContext = string.Join("\n",
            outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [new ChatMessage(ChatRole.User, "I strike the plague priest with my staff!")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)]);

        var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
        Assert.NotNull(metric.Value);
        Assert.True(metric.Value >= EvalSupport.GroundednessPassBar,
            $"Groundedness {metric.Value}: narration drifted from tool results. Narrative: {outcome.Narrative}");
    }
}
