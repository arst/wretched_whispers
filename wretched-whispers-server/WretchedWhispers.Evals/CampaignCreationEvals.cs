using Microsoft.Extensions.AI.Evaluation;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

public class CampaignCreationEvals
{
    private const string Suite = "campaign-creation";

    /// <summary>The opening turn now receives a finished wretch. It must configure the campaign and narrate
    /// that character -- naming the class the player picked and the numbers the domain rolled.</summary>
    [Fact]
    public async Task Opening_ConfiguresCampaignAndNarratesTheRolledWretch()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Opening-NarratesRolledWretch", [new ToolCallOrderEvaluator()]);
        await using var host = await EvalHost.CreateOpeningAsync(
            scenario.ChatClient, "Halvard", CharacterClass.FangedDeserter);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("begin");

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(["ConfigureCampaign"])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected [ConfigureCampaign]; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.Contains("Halvard", outcome.Narrative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deserter", outcome.Narrative, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The regression this whole flow exists to prevent: the player already chose a name and a
    /// class on the form, so the opening must not interrogate them for either.</summary>
    [Fact]
    public async Task Opening_DoesNotAskForNameOrClass()
    {
        await using var scenario = await EvalScenario.StartAsync(
            Suite, "Opening-DoesNotReAsk", [new NarrativeCheckEvaluator()]);
        await using var host = await EvalHost.CreateOpeningAsync(
            scenario.ChatClient, "Ysolde", CharacterClass.OccultHerbmaster);
        var outcome = await host.CreateTurnRunner().RunTurnAsync("begin");

        EvaluationResult result = await scenario.Run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new NarrativeCheckContext(
                "The player already chose their character's name and class before this scene. The "
                + "narration must NOT ask the player to provide, choose, or confirm a name or a class.")]);

        var metric = result.Get<BooleanMetric>(NarrativeCheckEvaluator.MetricName);
        Assert.True(metric.Value,
            $"The opening re-asked for a name or class the player already chose. Narrative: {outcome.Narrative}");
    }
}
