using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
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
        Assert.SkipWhen(chatClient is null, "Azure OpenAI credentials not configured; skipping live eval.");

        var reporting = CreateReportingConfiguration(chatClient!);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn1-Begin");

        await using var host = await EvalHost.CreateAsync(run.ChatConfiguration!.ChatClient);
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
        Assert.SkipWhen(chatClient is null, "Azure OpenAI credentials not configured; skipping live eval.");

        var reporting = CreateReportingConfiguration(chatClient!);
        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn2-Name");

        await using var host = await EvalHost.CreateAsync(run.ChatConfiguration!.ChatClient);

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

    // Mirrors AgentConfiguration's Azure wiring. Configuration section AzureOpenAiSettings binds
    // from env vars AzureOpenAiSettings__Endpoint, AzureOpenAiSettings__ApiKey, and
    // AzureOpenAiSettings__ChatModelDeployment (ASP.NET Core double-underscore convention).
    // Returns null if any are absent so the [Fact] skips cleanly.
    private static IChatClient? TryCreateAzureChatClient()
    {
        var endpoint = Environment.GetEnvironmentVariable("AzureOpenAiSettings__Endpoint");
        var apiKey = Environment.GetEnvironmentVariable("AzureOpenAiSettings__ApiKey");
        var deployment = Environment.GetEnvironmentVariable("AzureOpenAiSettings__ChatModelDeployment");
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(deployment))
            return null;

        var azure = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        return azure.GetChatClient(deployment).AsIChatClient();
    }
}
