using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Evals.Evaluators;
using WretchedWhispers.Evals.Harness;
using Xunit;

namespace WretchedWhispers.Evals;

public class CampaignCreationEvals
{
    private static readonly string[] CreateCampaignTools =
        ["CreateCharacter", "ConfigureCampaign", "StartCampaign"];

    [Fact]
    public async Task Turn1_Begin_CallsNoTools()
    {
        var chatClient = TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReportingConfiguration(chatClient);
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

    [Fact]
    public async Task Turn2_Name_CreatesCampaignInOrder()
    {
        var chatClient = TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReportingConfiguration(chatClient);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation-Turn2-Name");

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);

        // Turn 1 first so the model has asked for a name and history is consistent.
        await host.CreateTurnRunner().RunTurnAsync("begin");
        var outcome = await host.CreateTurnRunner().RunTurnAsync("Grim");

        EvaluationResult result = await run.EvaluateAsync(
            messages: [],
            modelResponse: outcome.Response,
            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);

        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
        Assert.True(metric.Value,
            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");
    }

    [Fact]
    public async Task Combat_InventoryQuestion_AnswersWithoutTakingTurn()
    {
        var chatClient = TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReportingConfiguration(chatClient);
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
        var chatClient = TryCreateAzureChatClient();
        if (chatClient is null)
        {
            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
            return;
        }

        var reporting = CreateReportingConfiguration(chatClient);
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

    // DiskBasedReportingConfiguration.Create signature (verified against 10.6.0):
    //   Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching,
    //          timeToLiveForCacheEntries, cachingKeys, executionName,
    //          evaluationMetricInterpreter, tags)
    // The plan omitted timeToLiveForCacheEntries (nullable TimeSpan) — we pass null to use the default.
    // ChatConfiguration is not IAsyncDisposable; no await using needed.
    private static ReportingConfiguration CreateReportingConfiguration(IChatClient chatClient)
    {
        var chatConfiguration = new ChatConfiguration(chatClient);
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: Path.Combine(AppContext.BaseDirectory, ".eval-results"),
            evaluators: [new ToolCallOrderEvaluator()],
            chatConfiguration: chatConfiguration,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: null,
            executionName: "campaign-creation");
    }

    // Mirrors AgentConfiguration's Azure wiring. Values can come from appsettings, user secrets, or
    // environment variables. Returns null if any are absent so the [Fact] skips cleanly.
    private static IChatClient? TryCreateAzureChatClient()
    {
        var settings = LoadAzureOpenAiSettings();
        if (string.IsNullOrWhiteSpace(settings.Endpoint)
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ChatModelDeployment))
            return null;

        var azure = new AzureOpenAIClient(new Uri(settings.Endpoint), new ApiKeyCredential(settings.ApiKey));
        return azure.GetChatClient(settings.ChatModelDeployment).AsIChatClient();
    }

    private static AzureOpenAiSettings LoadAzureOpenAiSettings()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<CampaignCreationEvals>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var section = config.GetSection(nameof(AzureOpenAiSettings));
        var settings = section.Get<AzureOpenAiSettings>() ?? new AzureOpenAiSettings();

        return new AzureOpenAiSettings
        {
            Endpoint = FirstNonEmpty(settings.Endpoint, config["AzureOpenAiSettings_Endpoint"]),
            ApiKey = FirstNonEmpty(settings.ApiKey, config["AzureOpenAiSettings_ApiKey"]),
            ChatModelDeployment = FirstNonEmpty(
                settings.ChatModelDeployment,
                config["AzureOpenAiSettings_ChatModelDeployment"])
        };
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

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
