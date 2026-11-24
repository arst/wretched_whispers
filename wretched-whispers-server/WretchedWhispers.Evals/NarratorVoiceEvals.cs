using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

/// <summary>
/// Everything the model writes is shown to the player as the story, as it is written — nothing is
/// filtered. So the narration must never talk about the machinery behind it: tool names, "the
/// domain", result field names, restated instructions, notes to itself. Seen in play: "(Remember:
/// after StartEncounter the next message resolves the first round of combat.)", "the domain reports
/// the worst — isDead true", "I'll check the pack and call the appropriate tool", "The assistant is
/// to respond as GM."
///
/// Whether a given turn leaks varies from one model run to the next, so this is a rate, not a
/// yes/no: each kind of turn is sampled several times and the suite passes while the number of
/// leaking samples stays at or under <see cref="MaxLeaks"/>. Two checks per sample:
/// <see cref="EngineTalk"/> (exact, free, names the word) and an LLM judge for what no word list
/// catches.
/// </summary>
public class NarratorVoiceEvals
{
    private const string Suite = "narrator-voice";
    private const int SamplesPerTurn = 3;

    /// <summary>
    /// Leaking samples tolerated out of all of them. Measured on gpt-5-mini: 5 of 18 before the
    /// persona's "every word you write is shown to the player" rule, between 0 and 2 of 18 with it.
    /// Zero would fail on the model's own randomness whenever the cached responses are refreshed.
    /// </summary>
    private const int MaxLeaks = 2;

    private const string Criterion =
        "This text is shown to the player, word for word, as the game master's narration in a tabletop "
        + "RPG. It must read as a game master speaking to a player at the table. FAIL it if any part "
        + "talks about the software behind the game or about the narrator's own instructions or "
        + "process: mentioning tools, functions, an engine, a system or 'the domain' that decides, rolls "
        + "or reports things; raw field names or data; restating rules about how turns or messages are "
        + "processed; notes or reminders addressed to itself or to 'the assistant'; or saying it will "
        + "call, check, or record something with a tool. Ordinary table talk is fine and must NOT fail: "
        + "dice rolls, modifiers and difficulty numbers, hit points and other stats, naming the game's own "
        + "rules and tables (a reaction roll, the reaction table), saying an action will need a test or "
        + "a roll, listing options, and asking the player what they do.";

    private sealed record Turn(string Name, Func<IChatClient, Task<EvalHost>> CreateHost, string PlayerMessage);

    // One of each kind of turn the leaks were seen in.
    private static readonly Turn[] Turns =
    [
        new("Voice-Opening",
            client => EvalHost.CreateOpeningAsync(client, "Halvard", CharacterClass.FangedDeserter),
            "begin"),
        new("Voice-LookAround",
            client => EvalHost.CreateExplorationAsync(client),
            "I look around carefully and check my gear."),
        new("Voice-FirstMeeting",
            client => EvalHost.CreateExplorationAsync(client),
            "A hooded figure waits at the crossroads shrine, face hidden, intent unknowable. I approach slowly and hail them."),
        new("Voice-RiskyFeat",
            client => EvalHost.CreateExplorationAsync(client),
            "I climb the sheer face of the bell tower in the driving rain, fingers on rotten mortar, "
            + "up to the arrow-slit window above."),
        new("Voice-CombatEntry",
            client => EvalHost.CreateExplorationAsync(client),
            "A bandit blocks the road with a rusty knife. I don't wait — I swing my staff at his skull!"),
        new("Voice-CombatRound",
            client => EvalHost.CreateCombatAsync(client),
            "I strike the plague priest with my staff!"),
    ];

    /// <summary>
    /// A question is where the machinery showed most in simulated play (43 of 275 turns in the run of
    /// 23 November 2026): the narrator answers by describing its own procedure. Each message below is
    /// one a simulated player sent, with the scene added where the question needs one; the quoted
    /// text is what came back.
    /// </summary>
    private static readonly Turn[] QuestionTurns =
    [
        // "I must call the tool that spends or adds it and then narrate the result."
        new("Voice-Question-HowToPlay",
            client => EvalHost.CreateExplorationAsync(client),
            "ok. i dont really know what to do. how do i win"),
        // "tell me how long you want to rest and I will call the rest tool"
        new("Voice-Question-Healing",
            client => EvalHost.CreateExplorationAsync(client),
            "how do i heal. is there a potion"),
        // "buy the three-tin bundle for 8 silver (I'll call BuyItem) ... sell the Sword for 15 now (I'll call SellItem)"
        new("Voice-Question-Prices",
            client => EvalHost.CreateExplorationAsync(client),
            "I stop at the peddler's cart by the well. i need food. 3 tins from you, and what do you pay "
            + "for my sword if i sell it back"),
        // "If you choose to rest here I will call the rest tool (it will advance time and apply whatever recovery the rules grant)."
        new("Voice-Question-RestBeforehand",
            client => EvalHost.CreateExplorationAsync(client),
            "Is there a quiet spot here where I could sit down and rest for a while? Please tell me how "
            + "much the rest will heal before I do it, and whether I need to eat."),
        // "if you attack I must call TurnEncounterHostile and then StartEncounter"
        new("Voice-Question-WhatIfIAttack",
            client => EvalHost.CreateExplorationAsync(client),
            "Two scavengers are picking through a broken wagon ahead and have not seen me. What are my "
            + "options? What happens if I just attack them?"),
        // "You check your pack again -- honest and blunt, the tools back this up."
        new("Voice-Question-Inventory",
            client => EvalHost.CreateExplorationAsync(client),
            "Before anything else, what do I have that could splint a crushed foot? Tell me exactly what "
            + "is in my pack."),
        // "The tool call is the table's truth" / "I will call the combat resolution tool"
        new("Voice-Question-CombatRules",
            client => EvalHost.CreateCombatAsync(client),
            "Hold on, before I swing. On a miss, does he get a free swing at me before my next turn? "
            + "And what is the number I need to roll?"),
        new("Voice-Question-CombatUseItem",
            client => EvalHost.CreateCombatAsync(client),
            "what happens if i stop to eat or use something from my pack right now, do i lose my attack"),
    ];

    /// <summary>
    /// Leaking samples tolerated out of the question turns' 24. Measured on gpt-5-mini: 15 of 24 before
    /// the persona's rule for answering questions and the reminder that closes the instructions; with
    /// the rule alone 4 to 7 of 24, with the reminder alone 10 of 48, with both 2 to 3 of 48.
    /// </summary>
    private const int MaxQuestionLeaks = 2;

    [Fact]
    public Task Narration_RarelyTalksAboutTheMachinery() => AssertRarelyLeaksAsync(Turns, MaxLeaks);

    [Fact]
    public Task AnswerToAQuestion_RarelyTalksAboutTheMachinery() =>
        AssertRarelyLeaksAsync(QuestionTurns, MaxQuestionLeaks);

    private static async Task AssertRarelyLeaksAsync(Turn[] turns, int maxLeaks)
    {
        var ct = TestContext.Current.CancellationToken;

        // Kinds of turn run side by side; a kind's samples run one after another.
        var leaksPerTurn = await Task.WhenAll(turns.Select(async turn =>
        {
            var leaks = new List<string>();
            for (var sample = 1; sample <= SamplesPerTurn; sample++)
                if (await FindLeakAsync(turn, sample, ct) is { } leak)
                    leaks.Add(leak);
            return leaks;
        }));

        var leaking = leaksPerTurn.SelectMany(leaks => leaks).ToList();
        Assert.True(leaking.Count <= maxLeaks,
            $"{leaking.Count} of {turns.Length * SamplesPerTurn} sampled turns talk about the machinery "
            + $"(at most {maxLeaks} allowed).\n\n{string.Join("\n\n", leaking)}");
    }

    /// <summary>Runs one sample of a turn; returns a description of the leak, or null when it is clean.</summary>
    private static async Task<string?> FindLeakAsync(Turn turn, int sample, CancellationToken ct)
    {
        // A run with nothing cached makes dozens of model calls at once and now and then loses one: the
        // turn comes back as an error, or the judge returns no verdict. That is the deployment, not the
        // narrator, so a lost call is tried once more before the sample counts as failed.
        for (var attempt = 1; ; attempt++)
        {
            await using var scenario = await EvalScenario.StartAsync(
                Suite, turn.Name, [new NarrativeCheckEvaluator()], sample: sample);
            await using var host = await turn.CreateHost(scenario.ChatClient);

            var outcome = await host.CreateTurnRunner().RunTurnAsync(turn.PlayerMessage, ct);
            BooleanMetric? verdict = null;
            if (outcome.Error is null)
            {
                EvaluationResult result = await scenario.Run.EvaluateAsync(
                    messages: [], modelResponse: outcome.Response,
                    additionalContext: [new NarrativeCheckContext(Criterion)], cancellationToken: ct);
                result.TryGet(NarrativeCheckEvaluator.MetricName, out verdict);
            }

            if (verdict is null)
            {
                Assert.True(attempt < 2, $"{turn.Name} sample {sample}: the call was lost twice (turn error: "
                    + $"{outcome.Error ?? "none"}; judge verdict: none).");
                continue;
            }

            var engineTerms = EngineTalk.Find(outcome.Narrative);
            return engineTerms.Count == 0 && verdict.Value == true
                ? null
                : $"{turn.Name} sample {sample} — engine terms: [{string.Join(", ", engineTerms)}]; "
                  + $"judge: {(verdict.Value == true ? "pass" : "FAIL")}. Narrative: {outcome.Narrative}";
        }
    }
}
