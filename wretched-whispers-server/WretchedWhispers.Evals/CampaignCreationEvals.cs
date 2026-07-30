using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using WretchedWhispers.Core.Characters.Classes;
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

    /// <summary>Creation is name -> class -> create. Giving a name must NOT create anything yet: the narrator
    /// owes the player a class choice first, and creating early would silently decide it for them.</summary>
    [Fact]
    public async Task Turn2_Name_AsksForClassWithoutCallingTools()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn2-Name-AsksClass");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);

        // Turn 1 first so the model has asked for a name and history is consistent.
        await host.CreateTurnRunner().RunTurnAsync("begin");
        var outcome = await host.CreateTurnRunner().RunTurnAsync("Grim");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext([])]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected no tools until a class is chosen; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.True(
            OffersAClass(outcome.Narrative),
            $"Expected the narrator to offer classes by name. Narrative: {outcome.Narrative}");
    }

    /// <summary>The class the player picks has to be the class the domain builds -- this asserts the created
    /// character, not the narration around it.</summary>
    [Fact]
    public async Task Turn3_ChosenClass_CreatesThatClass()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn3-ChosenClass");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);

        await host.CreateTurnRunner().RunTurnAsync("begin");
        await host.CreateTurnRunner().RunTurnAsync("Grim");
        var outcome = await host.CreateTurnRunner().RunTurnAsync("A fanged deserter.");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");
        Assert.Contains("Fanged Deserter", CreateCharacterResult(outcome));
    }

    /// <summary>"Roll for me" must still produce a real class -- and never Classless, which is only ever an
    /// explicit choice. The die belongs to the domain, so the tool is called with the class omitted.</summary>
    [Fact]
    public async Task Turn3_RollRequest_CreatesARolledClass()
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, [new ToolCallOrderEvaluator()], "campaign-creation");
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn3-RolledClass");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);

        await host.CreateTurnRunner().RunTurnAsync("begin");
        await host.CreateTurnRunner().RunTurnAsync("Grim");
        var outcome = await host.CreateTurnRunner().RunTurnAsync("I don't care, roll for it.");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");

        // A rolled class is a real class: the tool result must name one of the six, never Classless Scum.
        var created = CreateCharacterResult(outcome);
        Assert.Contains(
            ClassPresets.Rollable.Select(c => ClassPresets.For(c).DisplayName),
            name => created.Contains(name, StringComparison.Ordinal));
        Assert.DoesNotContain(ClassPresets.For(CharacterClass.Classless).DisplayName, created);
    }

    /// <summary>The serialized CreateCharacter result -- what the domain actually built, rather than what the
    /// narration claims about it.</summary>
    private static string CreateCharacterResult(TurnOutcome outcome)
    {
        var toolResult = outcome.ToolResults.SingleOrDefault(r => r.Function == "CreateCharacter")
            ?? throw new InvalidOperationException(
                $"CreateCharacter was not called; tools were [{string.Join(", ", outcome.ToolCalls)}]");
        return toolResult.Result?.ToString() ?? "";
    }

    private static bool OffersAClass(string narrative) =>
        ClassPresets.Rollable
            .Select(c => ClassPresets.For(c).DisplayName)
            .Count(name => narrative.Contains(name, StringComparison.OrdinalIgnoreCase)) >= 3;

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
