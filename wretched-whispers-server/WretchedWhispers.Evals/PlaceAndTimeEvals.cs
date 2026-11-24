using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

/// <summary>
/// The sheet says where the party is and what hour it is, and the doom clock runs on that hour. In
/// simulated play neither kept up with the story: six of eleven games never had a place on the sheet at
/// all, two 30-turn games advanced one hour and none, and 13 of 38 replies that stated the hour stated
/// one the sheet did not have (run of 23 November 2026).
///
/// Each scenario is sampled three times and reads the campaign's own state after the turn.
/// </summary>
public partial class PlaceAndTimeEvals
{
    private const string Suite = "place-and-time";
    private const int Samples = 3;

    private sealed record After(string? Place, int Day, int Hour, int HoursPassed, TurnOutcome Outcome);

    [Fact]
    public async Task Opening_PutsTheStartingPlaceOnTheSheet()
    {
        var runs = await RunAsync(
            "Opening-StartingPlace-OnTheSheet",
            client => EvalHost.CreateOpeningAsync(client, "Halvard", CharacterClass.FangedDeserter),
            "begin");

        AssertAll(runs, run => !string.IsNullOrWhiteSpace(run.Place),
            "The opening describes where the character wakes, and the sheet must name that place");
    }

    [Fact]
    public async Task Exploration_Arrival_MovesThePartyOnTheSheet()
    {
        var runs = await RunAsync(
            "Exploration-Arrival-SetsLocation",
            client => EvalHost.CreateExplorationAsync(client),
            "I follow the old salt road north until the walls of Galgenbeck rise ahead, and I walk in "
            + "through the gate.");

        AssertAll(runs, run => run.Place is not null && run.Place.Contains("Galgenbeck", StringComparison.OrdinalIgnoreCase),
            "The party arrived in Galgenbeck and the sheet must say so");
        AssertAll(runs, run => run.HoursPassed >= 1, "A journey by road takes time and the clock must move");
    }

    [Fact]
    public async Task Exploration_TimeConsumingAction_MovesTheClock()
    {
        var runs = await RunAsync(
            "Exploration-Travel-AdvancesTime",
            client => EvalHost.CreateExplorationAsync(client),
            "I search the burnt farmhouse room by room for anything worth taking.");

        AssertAll(runs, run => run.HoursPassed >= 1, "Searching a house room by room takes time and the clock must move");
    }

    /// <summary>Whatever hour or day a reply states must be the one on the sheet after the turn.</summary>
    private static string? WrongClock(After run)
    {
        foreach (Match match in StatedClock().Matches(run.Outcome.Narrative))
        {
            var isDay = match.Groups["unit"].Value.Equals("day", StringComparison.OrdinalIgnoreCase);
            var stated = int.Parse(match.Groups["n"].Value);
            if (stated != (isDay ? run.Day : run.Hour))
                return $"says \"{match.Value}\" but the sheet has day {run.Day}, hour {run.Hour}";
        }

        return null;
    }

    // "Day 1, Hour 3", "hour is 3", "Hour: 3". Not "an hour" or "three hours": those are durations.
    [GeneratedRegex(@"\b(?<unit>Day|Hour)(?: is| now|:)?\s+(?<n>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatedClock();

    private static void AssertAll(IReadOnlyList<After> runs, Func<After, bool> holds, string what)
    {
        var failed = runs.Select((run, i) => (run, sample: i + 1))
            .Where(x => !holds(x.run) || WrongClock(x.run) is not null)
            .Select(x => $"sample {x.sample}: place '{x.run.Place}', {x.run.HoursPassed} hours passed, "
                + $"{WrongClock(x.run) ?? "clock stated right or not at all"}, tools "
                + $"[{string.Join(", ", x.run.Outcome.ToolCalls)}]")
            .ToList();

        Assert.True(failed.Count == 0, $"{what}. Failed in {failed.Count} of {runs.Count} runs: {string.Join("; ", failed)}");
    }

    private static async Task<IReadOnlyList<After>> RunAsync(
        string scenarioName, Func<IChatClient, Task<EvalHost>> createHost, string playerMessage)
    {
        var ct = TestContext.Current.CancellationToken;
        var runs = new List<After>();
        for (var sample = 1; sample <= Samples; sample++)
        {
            await using var scenario = await EvalScenario.StartAsync(Suite, scenarioName, [], sample: sample);
            await using var host = await createHost(scenario.ChatClient);
            var before = await ClockAsync(host, ct);

            var outcome = await host.CreateTurnRunner().RunTurnAsync(playerMessage, ct);
            Assert.Null(outcome.Error);

            var (place, day, hour) = await ClockAsync(host, ct);
            runs.Add(new After(place, day, hour, (day - before.Day) * 24 + hour - before.Hour, outcome));
        }

        return runs;
    }

    private static Task<(string? Place, int Day, int Hour)> ClockAsync(EvalHost host, CancellationToken ct) =>
        host.QueryAsync(async sp =>
        {
            var context = await sp.GetRequiredService<ISessionContextLoader>().LoadAsync(host.SessionId, ct);
            var campaign = context.Campaign ?? throw new InvalidOperationException("No campaign in the eval session.");
            return (campaign.CurrentLocationName, campaign.CurrentDay, campaign.CurrentHour);
        });
}
