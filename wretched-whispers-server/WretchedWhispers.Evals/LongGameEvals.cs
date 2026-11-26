using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

/// <summary>
/// Third and fourth simulated runs: late in a game the story ran ahead of the sheet. A purchase was told
/// done after a haggle with nothing bought, a stolen pan was sold with no tool, a chest the player declared
/// was told as "Gain: 300 silver", gifts stayed in the pack. One-turn evals of the same requests passed, so
/// this plays one game of several turns, each modelled on a verified incident, and checks every reply
/// against what that turn changed.
/// </summary>
public class LongGameEvals
{
    private const string Suite = "long-game";

    private static readonly string[] Turns =
    [
        "I walk into the market square of Gallowmere and look for anyone selling food.",
        "I haggle with the bread-seller over a strip of jerky and a black loaf, and buy both if the price is fair.",
        "A beggar child sits by the well. I give her my small bronze bell.",
        "While the tinker looks the other way, I swipe an iron pan from his cart.",
        "I sell the pan to the drover by the gate for whatever he'll pay.",
        "I remember the chest of 300 silver I buried under the gallows last week. I dig it up and take it all.",
        "I pay the old gravedigger 5 silver to tell me where the abbot is buried.",
        "how much silver do i have now, and what's in my pack?",
    ];

    [Fact]
    public async Task LongGame_EveryReplyMatchesWhatTheTurnChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var failures = new List<string>();
        var judged = 0;
        for (var sample = 1; sample <= Samples; sample++)
        {
            await using var scenario = await EvalScenario.StartAsync(Suite, "LongGame-StoryFollowsTheSheet", [], sample: sample);
            await using var host = await EvalHost.CreateExplorationAsync(scenario.ChatClient,
                [new InventoryItem(Guid.NewGuid(), "small bronze bell", isBulky: false, isOneTimeUse: false, quantity: 1)],
                maxHp: 12); // Tuck's usual 2 died to a failed theft halfway through the script.

            for (var turn = 0; turn < Turns.Length; turn++)
            {
                var outcome = await host.CreateTurnRunner().RunTurnAsync(Turns[turn], ct);
                Assert.Null(outcome.Error);
                var sheet = await SheetAsync(host, ct);
                var delta = outcome.Delta;
                var change = delta?.SilverChange ?? 0;
                var added = delta?.ItemsAdded ?? [];
                var removed = delta?.ItemsRemoved ?? [];

                judged++;
                // A scenario run is judged once, so each turn is judged in a run of its own.
                await using var judge = await EvalScenario.StartAsync(
                    Suite, $"LongGame-StoryFollowsTheSheet-Turn{turn + 1}", [new NarrativeCheckEvaluator()], sample: sample);
                var passed = await JudgeAsync(judge, outcome.Narrative,
                    $"This turn the character's silver changed by {change} and is now {sheet.Silver}. Items added to "
                    + $"the pack this turn: [{string.Join(", ", added)}]. Items removed: [{string.Join(", ", removed)}]. "
                    + $"The pack now holds: [{sheet.Pack}]. FAIL if the narration states or implies a change to "
                    + "silver or the pack that these do not show: silver gained, paid or spent, something bought, "
                    + "taken, stolen, sold, given away or used up that did not enter or leave the pack, or a silver "
                    + $"total other than {sheet.Silver}. Changes the narration leaves unmentioned pass, and so do "
                    + "attempts that are told as failed, refused or not yet done. Scrolls, food, weapon and armor are on the "
                    + "sheet outside the pack, so naming them passes, and so do prices offered and not yet paid.", ct);
                if (passed != true)
                    failures.Add($"sample {sample}, turn {turn + 1} (\"{Turns[turn]}\"): silver {change:+#;-#;0} to "
                        + $"{sheet.Silver}, added [{string.Join(", ", added)}], removed [{string.Join(", ", removed)}], "
                        + $"tools [{string.Join(", ", outcome.ToolCalls)}], narrative: {outcome.Narrative}");
            }
        }

        Assert.True(failures.Count <= MaxMismatches, $"A reply told the sheet wrong in {failures.Count} of {judged} "
            + $"turns (at most {MaxMismatches} allowed): " + string.Join("; ", failures));
    }

    private const int Samples = 3;

    /// <summary>Measured on gpt-5-mini over 24 turns: 11 before a test's result said in words what a pass and a
    /// fail mean (a haggle told as a purchase, a stolen pan told as taken, silver totals with a payment taken
    /// off twice), 4 after, 2 with the judge told that scrolls and food sit outside the pack and the silver
    /// rule saying Game State already holds every earlier turn. One of those two was still a payment taken off
    /// twice. At one in twelve, 24 turns hold 4 or fewer about 19 times in 20.</summary>
    private const int MaxMismatches = 4;

    private static Task<(int Silver, string Pack)> SheetAsync(EvalHost host, CancellationToken ct) =>
        host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>().LoadAsync(host.SessionId, ct);
            var character = context.Character ?? throw new InvalidOperationException("No character in the eval session.");
            return (character.Silver, string.Join(", ", character.Inventory.InventoryItems
                .Select(i => i.Quantity > 1 ? $"{i.Description} x{i.Quantity}" : i.Description)));
        });

    private static async Task<bool?> JudgeAsync(EvalScenario scenario, string text, string criterion, CancellationToken ct)
    {
        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [], modelResponse: new ChatResponse(new ChatMessage(ChatRole.Assistant, text)),
            additionalContext: [new NarrativeCheckContext(criterion)], cancellationToken: ct);
        return result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName).Value;
    }
}
