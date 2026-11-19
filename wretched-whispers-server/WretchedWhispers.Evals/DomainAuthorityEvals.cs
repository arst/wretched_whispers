using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Engine.Services;
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
            Suite, scenarioName, [new ToolCallEvaluator(ordered: false)]);
        await using var host = await createHost(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(playerMessage, TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext([requiredTool])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
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
            client => EvalHost.CreateCombatAsync(client),
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
            Suite, "Combat-InventoryQuestion-NoTurn", [new ToolCallEvaluator(ordered: true)]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("What do I have in my inventory and equipment?", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext([])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.OrderedMetricName);
        Assert.True(metric.Value, $"Expected no combat tools for an inventory question; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.Contains("staff", outcome.Narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Combat_MissingItemUse_DoesNotInventItemOrTakeTurn()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-MissingItemUse-NoTurn", [new ToolCallEvaluator(ordered: true), new NarrativeCheckEvaluator()]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I light my lantern and throw it at the priest.", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext:
            [
                new ToolCallsContext([]),
                new NarrativeCheckContext(
                    "The player tried to use a lantern their character does not possess. The narration "
                    + "must make clear the lantern is not available (in any wording), and must NOT "
                    + "narrate the lantern actually being lit, thrown, or otherwise used."),
            ], cancellationToken: TestContext.Current.CancellationToken);

        var orderMetric = result.Get<BooleanMetric>(ToolCallEvaluator.OrderedMetricName);
        Assert.True(orderMetric.Value,
            $"Expected no combat tools for a missing lantern; got [{string.Join(", ", outcome.ToolCalls)}]");

        var narrativeMetric = result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName);
        Assert.True(narrativeMetric.Value,
            $"Expected the GM to refuse the missing lantern. Narrative: {outcome.Narrative}");
    }

    [Fact]
    public async Task Exploration_ViolenceErupting_CreatesArmsAndStartsEncounter()
    {
        // The single most order-sensitive rule in the prompt: combat entry is
        // CreateEncounter -> AddAdversaryToEncounter -> StartEncounter. Narrating a fight without a
        // started encounter leaves the whole combat outside the domain.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Exploration-CombatEntry-OrderedChain", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateExplorationAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "A lone grave-robber springs from the ditch, knife first, straight at me. I meet him with my staff — fight!", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["CreateEncounter", "AddAdversaryToEncounter", "StartEncounter"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            "Combat entry requires CreateEncounter + AddAdversaryToEncounter + StartEncounter; "
            + $"got [{string.Join(", ", outcome.ToolCalls)}].");

        // Contains-mode tolerates extras (journaling); the CHAIN itself must still be in order.
        var calls = outcome.ToolCalls.ToList();
        Assert.True(
            calls.IndexOf("CreateEncounter") < calls.IndexOf("AddAdversaryToEncounter")
            && calls.LastIndexOf("AddAdversaryToEncounter") < calls.IndexOf("StartEncounter"),
            $"Combat entry chain out of order: [{string.Join(", ", calls)}].");
    }

    [Fact]
    public async Task Exploration_OpenFirstMeeting_RollsReactionViaUnknownEncounter()
    {
        // A first meeting whose attitude the fiction leaves open must be CreateEncounter with type
        // 'Unknown' so the DOMAIN rolls the Mörk Borg reaction table — the GM never decides the
        // stranger's attitude itself. A pre-declared Hostile/Friendly stores no reaction roll, so the
        // committed encounter is the deterministic witness of which path the model took.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Exploration-FirstMeeting-RollsReaction", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateExplorationAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "A hooded figure waits at the crossroads shrine, face hidden, intent unknowable. I approach slowly and hail them.", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["CreateEncounter"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            $"An open first meeting must create an encounter; got [{string.Join(", ", outcome.ToolCalls)}].");

        var (reaction, reactionRoll) = await host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>()
                .LoadAsync(host.SessionId, TestContext.Current.CancellationToken);
            return (context.ActiveEncounter?.Reaction, context.ActiveEncounter?.ReactionRoll);
        });
        Assert.True(reactionRoll is not null && reaction is not null,
            "The encounter carries no reaction roll — the GM pre-declared the attitude instead of "
            + "creating the encounter as 'Unknown' and letting the domain roll the reaction table.");
    }

    [Fact]
    public async Task Exploration_LightingTorch_ConsumesItemViaTool()
    {
        // The positive twin of the no-fabrication camp eval: when the fiction genuinely consumes a
        // carried item, UseItemFromCharacterInventory must record it — never prose alone.
        await AssertToolRequiredAsync(
            "Exploration-TorchLit-UsesItem",
            client => EvalHost.CreateExplorationAsync(
                client,
                [new InventoryItem(Guid.NewGuid(), "torches", isBulky: false, isOneTimeUse: true, quantity: 3)]),
            "I take one of my torches, strike it alight, and descend into the black crypt beneath the chapel.",
            "UseItemFromCharacterInventory",
            "Consuming a carried item must go through UseItemFromCharacterInventory.");
    }

    [Fact]
    public async Task Combat_CastingScroll_SpendsUseThenResolvesRoundAsOther()
    {
        // The in-combat 'Other' path: a scroll cast is resolved by CastScroll (spends the use), then
        // ResolveCombatRound with the enemies responding — exactly one round, cast before round.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-CastScroll-ThenRoundOther", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateCombatAsync(
            scenario.ChatClient,
            scrolls: [new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, "Palms Open the Southern Gate")]);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I unfurl Palms Open the Southern Gate and hurl its power at the priest!", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["CastScroll", "ResolveCombatRound"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            "An in-combat cast must spend the scroll (CastScroll) and let the enemies respond "
            + $"(ResolveCombatRound); got [{string.Join(", ", outcome.ToolCalls)}].");

        var calls = outcome.ToolCalls.ToList();
        Assert.True(calls.IndexOf("CastScroll") < calls.IndexOf("ResolveCombatRound"),
            $"CastScroll must precede ResolveCombatRound: [{string.Join(", ", calls)}].");
        Assert.Equal(1, calls.Count(c => c == "ResolveCombatRound"));
    }

    // Most scenarios stay single-turn as focused regression guards. Multi-turn scenarios are viable
    // because tool results are deterministic (no ids in the DTOs, seeded eval dice) — every
    // completion in a turn cache-hits, not just the first; Combat_FightingToTheDeath_DeathStaysFinal
    // is the multi-turn proof.

    [Fact]
    public async Task Resolution_Loot_AddsItemViaTool()
    {
        // Resolution-stage loot must land in inventory through the tool, never prose alone.
        await AssertToolRequiredAsync(
            "Resolution-Loot-AddsItem",
            EvalHost.CreateResolutionAsync,
            "I pry the brass crucible from the dead priest's fingers and stow it in my satchel.",
            "AddItemToCharacterInventory",
            "Loot must go through AddItemToCharacterInventory.");
    }

    [Fact]
    public async Task Resolution_MovingOn_CompletesResolutionBackToExploration()
    {
        // Leaving the scene ends the aftermath: CompleteResolution must be called, and the DOMAIN's
        // derived stage must be Exploration again afterwards — otherwise the session wedges in
        // Resolution forever (the playtest failure this eval guards).
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Resolution-MovingOn-Completes", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateResolutionAsync(scenario.ChatClient);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "The priest is dead and the road is quiet again. Nothing more holds me here — I shake the blood from my staff and walk on into the mist.", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["CompleteResolution"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            $"Leaving the aftermath must call CompleteResolution; got [{string.Join(", ", outcome.ToolCalls)}].");

        var stage = await host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>()
                .LoadAsync(host.SessionId, TestContext.Current.CancellationToken);
            return context.DeriveStage();
        });
        Assert.Equal(SessionStage.Exploration, stage);
    }

    /// <summary>Loads the committed character through the session context — the domain's truth for
    /// omen counts, HP, and death, independent of anything the model narrated.</summary>
    private static Task<Character> LoadCharacterAsync(EvalHost host) =>
        host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>()
                .LoadAsync(host.SessionId, TestContext.Current.CancellationToken);
            var characterId = context.CharacterId
                ?? throw new InvalidOperationException("No character in the eval session.");
            return await sp.GetRequiredService<ICharactersRepository>().Get(characterId)
                ?? throw new InvalidOperationException("Character not found.");
        });

    [Fact]
    public async Task Combat_OmenSpend_SpendsARealOmen()
    {
        // The player explicitly spends an omen. ResolveCombatRound must carry the spend, and the
        // DOMAIN's omen count is the witness: narrating "fate favors you" without the omenUse
        // argument leaves the count at 2 and fails this eval. Dice-dependent (the round result embeds
        // rolls) — writable since the eval dice were seeded.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-OmenSpend-MaxDamage", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient, omens: 2);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I spend an omen to guide my arm -- all my luck behind one blow -- and strike the priest with my staff!", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["ResolveCombatRound"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            $"Expected a ResolveCombatRound call; got [{string.Join(", ", outcome.ToolCalls)}].");
        Assert.Equal(1, outcome.ToolCalls.Count(c => c == "ResolveCombatRound"));

        var character = await LoadCharacterAsync(host);
        Assert.True(character.Omens.Count == 1,
            $"The player asked to spend an omen but the domain still holds {character.Omens.Count} of 2 — "
            + "the round was resolved without the omenUse spend (or spent more than one).");
    }

    [Fact]
    public async Task Exploration_OmenSpend_LowersChallengeDr()
    {
        // The exploration twin: an omen spent on an ability test must ride ChallengeCharacter's
        // spendOmenToLowerDr flag, and again the committed omen count is the proof.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Exploration-OmenSpend-LowersDr", [new ToolCallEvaluator(ordered: false)]);
        await using var host = await EvalHost.CreateExplorationAsync(scenario.ChatClient, omens: 2);
        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "The rotten rope bridge sways over the gorge. I dart across before it gives way -- "
            + "and I spend an omen to steady my fate.", TestContext.Current.CancellationToken);

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new ToolCallsContext(["ChallengeCharacter"])], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<BooleanMetric>(ToolCallEvaluator.ContainsMetricName);
        Assert.True(metric.Value,
            $"Expected a ChallengeCharacter call; got [{string.Join(", ", outcome.ToolCalls)}].");

        var character = await LoadCharacterAsync(host);
        Assert.True(character.Omens.Count == 1,
            $"The player asked to spend an omen but the domain still holds {character.Omens.Count} of 2 — "
            + "the challenge ran without spendOmenToLowerDr.");
    }

    [Fact]
    public async Task Exploration_RiskyFeat_ResolvesViaChallenge()
    {
        // A risky action with real, uncertain stakes must be resolved by ChallengeCharacter — the
        // domain rolls the test and applies the consequence. Regression guard against the GM
        // narrating success (or harm) of a dangerous feat without any roll.
        await AssertToolRequiredAsync(
            "Exploration-RiskyFeat-CallsChallenge",
            client => EvalHost.CreateExplorationAsync(client),
            "I climb the sheer face of the bell tower in the driving rain, fingers on rotten mortar, "
            + "up to the arrow-slit window above.",
            "ChallengeCharacter",
            "A risky feat must be resolved by ChallengeCharacter, never narrated success or failure.");
    }

    /// <summary>Runs the boolean narration judge over <paramref name="text"/>.</summary>
    private static async Task<bool?> JudgeAsync(EvalScenario scenario, string text, string criterion)
    {
        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, text)),
            additionalContext: [new NarrativeCheckContext(criterion)],
            cancellationToken: TestContext.Current.CancellationToken);
        return result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName).Value;
    }

    [Fact]
    public async Task Combat_Attack_StatesNoOutcomeBeforeTheRoll()
    {
        // All narration is shown as the model writes it, so prose written BEFORE the round is resolved
        // must not claim how it went: the dice have not been rolled yet. Wind-up and intent are fine.
        // This eval replaces the former executor rule that withheld pre-tool prose.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Combat-NoOutcomeBeforeTheRoll", [new NarrativeCheckEvaluator()]);
        await using var host = await EvalHost.CreateCombatAsync(scenario.ChatClient);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "I strike the plague priest with my staff!", TestContext.Current.CancellationToken);

        Assert.Contains("ResolveCombatRound", outcome.ToolCalls);
        // Calling the tool first and narrating only afterwards — what the prompt asks for — passes outright.
        if (string.IsNullOrWhiteSpace(outcome.NarrativeBeforeTools)) return;

        var passed = await JudgeAsync(scenario, outcome.NarrativeBeforeTools,
            "This text was written BEFORE the attack was resolved. It may describe intent, the wind-up, or "
            + "the scene, but it must NOT state or imply the result: whether the attack hits or misses, any "
            + "damage dealt or taken, or anyone being wounded, killed, or fleeing.");
        Assert.True(passed, $"Outcome narrated before the roll: {outcome.NarrativeBeforeTools}");
    }

    [Fact]
    public async Task Exploration_TakingAnItem_IsNeverNarratedWithoutTheStateChange()
    {
        // "Actions are not narrated without modifying the state": the player takes something. Either the
        // inventory really changes (a tool applied it — the delta shows the item), or the narration must
        // not say the character now has it.
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Exploration-ClaimedLoot-NeedsStateChange", [new NarrativeCheckEvaluator()]);
        await using var host = await EvalHost.CreateExplorationAsync(scenario.ChatClient);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(
            "There's a tarnished silver candlestick on the roadside shrine. I pocket it and walk on.",
            TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        if (outcome.Delta is { ItemsAdded.Length: > 0 }) return;

        var passed = await JudgeAsync(scenario, outcome.Narrative,
            "The character's inventory did NOT change this turn. The narration must not state or imply that "
            + "the character took, pocketed, or now carries the candlestick. Refusing, interrupting, or "
            + "describing an obstacle all pass.");
        Assert.True(passed,
            $"Narration claimed loot that no tool applied (calls: [{string.Join(", ", outcome.ToolCalls)}]). "
            + $"Narrative: {outcome.Narrative}");
    }

    [Fact]
    public async Task Combat_DoubleAction_ResolvesExactlyOneRound()
    {
        // The player describes two attacks in one message. One message is one round: the model must
        // call ResolveCombatRound once (code now rejects a second call, but the model should not try).
        var outcome = await AssertToolRequiredAsync(
            "Combat-DoubleAction-OneRound",
            client => EvalHost.CreateCombatAsync(client),
            "I smash the priest with my staff, then spin and smash him again before he can recover!",
            "ResolveCombatRound");

        Assert.Equal(1, outcome.ToolCalls.Count(c => c == "ResolveCombatRound"));
    }

    [Fact]
    public async Task Combat_TwoAdversaries_TargetsTheOneThePlayerMeans()
    {
        // Tool results never enter the chat history, so the model can only fill targetAdversaryName
        // correctly from the Game State snapshot's adversary list. The player names the target by
        // description, not by its stat-block name.
        var outcome = await AssertToolRequiredAsync(
            "Combat-TwoAdversaries-TargetsByName",
            client => EvalHost.CreateTwoAdversaryCombatAsync(client),
            "I ignore the priest and bring my staff down on the snarling dog!",
            "ResolveCombatRound");

        var round = Assert.Single(outcome.Calls, c => c.Name == "ResolveCombatRound");
        Assert.NotNull(round.Arguments);
        using var args = JsonDocument.Parse(round.Arguments);
        Assert.True(args.RootElement.TryGetProperty("targetAdversaryName", out var target),
            $"ResolveCombatRound was called without a target: {round.Arguments}");
        Assert.Equal("Grave Hound", target.GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Combat_FightingToTheDeath_DeathStaysFinal()
    {
        // THE fabrication class this project exists to prevent: the domain kills the wretch (0 HP,
        // then the broken d4 comes up death), and no narration may walk it back. Multi-turn: the
        // player keeps swinging until the seeded dice kill them, then tries to fight on from beyond
        // the grave — the coordinator must refuse that turn without ever asking the model. Only
        // writable now that whole turns are cache-stable (id-free DTOs + seeded dice), so the round
        // count is identical every run.
        await using var scenario = await EvalScenario.StartAsync(Suite, "Combat-DeathFight-DeathIsFinal", []);
        var recorder = new RequestCountingChatClient(scenario.ChatClient);
        await using var host = await EvalHost.CreateDeathFightAsync(recorder);

        const int maxRounds = 8;
        var dead = false;
        for (var round = 0; round < maxRounds && !dead; round++)
        {
            await host.CreateTurnRunner().RunTurnAsync(
                "The Bell-Warden will fall or I will. I swing my staff at it with everything I have!", TestContext.Current.CancellationToken);
            dead = (await LoadCharacterAsync(host)).IsDead;
        }

        Assert.True(dead, $"The seeded death-fight must kill the character within {maxRounds} rounds.");
        var stage = await host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>()
                .LoadAsync(host.SessionId, TestContext.Current.CancellationToken);
            return context.DeriveStage();
        });
        Assert.Equal(SessionStage.Ended, stage);

        // The wretch is dead and the stage is Ended: the coordinator refuses the turn before any model
        // call, so there is no narration to judge — the refusal itself is the guarantee.
        var requestsBefore = recorder.Count;
        var postDeath = await host.CreateTurnRunner().RunTurnAsync(
            "Death is not the end of me. I stand back up, take my staff, and keep fighting!", TestContext.Current.CancellationToken);

        Assert.NotNull(postDeath.Error);
        Assert.Empty(postDeath.Calls);
        Assert.Empty(postDeath.ToolResults);
        Assert.Empty(postDeath.Narrative);
        Assert.Equal(requestsBefore, recorder.Count);
        Assert.True((await LoadCharacterAsync(host)).IsDead, "The refused turn must leave the character dead.");
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
        var outcome = await host.CreateTurnRunner().RunTurnAsync(playerMessage, TestContext.Current.CancellationToken);

        var groundingContext =
            "Character inventory before the turn: torches x3. Inventory changes ONLY through a "
            + "UseItemFromCharacterInventory result below; without one, every count is unchanged.\n"
            + string.Join("\n", outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [new ChatMessage(ChatRole.User, playerMessage)],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)], cancellationToken: TestContext.Current.CancellationToken);

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
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I strike the plague priest with my staff!", TestContext.Current.CancellationToken);

        var groundingContext = string.Join("\n",
            outcome.ToolResults.Select(t => $"{t.Function}: {t.Result}"));

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [new ChatMessage(ChatRole.User, "I strike the plague priest with my staff!")],
            modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Narrative)),
            additionalContext: [new GroundednessEvaluatorContext(groundingContext)], cancellationToken: TestContext.Current.CancellationToken);

        var metric = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
        Assert.NotNull(metric.Value);
        Assert.True(metric.Value >= EvalSupport.GroundednessPassBar,
            $"Groundedness {metric.Value}: narration drifted from tool results. Narrative: {outcome.Narrative}");
    }
}
