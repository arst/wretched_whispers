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

    [Fact]
    public async Task Narration_RarelyTalksAboutTheMachinery()
    {
        var ct = TestContext.Current.CancellationToken;

        // Kinds of turn run side by side; a kind's samples run one after another.
        var leaksPerTurn = await Task.WhenAll(Turns.Select(async turn =>
        {
            var leaks = new List<string>();
            for (var sample = 1; sample <= SamplesPerTurn; sample++)
                if (await FindLeakAsync(turn, sample, ct) is { } leak)
                    leaks.Add(leak);
            return leaks;
        }));

        var leaking = leaksPerTurn.SelectMany(leaks => leaks).ToList();
        Assert.True(leaking.Count <= MaxLeaks,
            $"{leaking.Count} of {Turns.Length * SamplesPerTurn} sampled turns talk about the machinery "
            + $"(at most {MaxLeaks} allowed).\n\n{string.Join("\n\n", leaking)}");
    }

    /// <summary>Runs one sample of a turn; returns a description of the leak, or null when it is clean.</summary>
    private static async Task<string?> FindLeakAsync(Turn turn, int sample, CancellationToken ct)
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, turn.Name, [new NarrativeCheckEvaluator()], sample: sample);
        await using var host = await turn.CreateHost(scenario.ChatClient);

        var outcome = await host.CreateTurnRunner().RunTurnAsync(turn.PlayerMessage, ct);
        Assert.Null(outcome.Error);

        var engineTerms = EngineTalk.Find(outcome.Narrative);
        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: outcome.Response,
            additionalContext: [new NarrativeCheckContext(Criterion)], cancellationToken: ct);
        var judged = result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName).Value;

        return engineTerms.Count == 0 && judged == true
            ? null
            : $"{turn.Name} sample {sample} — engine terms: [{string.Join(", ", engineTerms)}]; "
              + $"judge: {(judged == true ? "pass" : "FAIL")}. Narrative: {outcome.Narrative}";
    }
}
