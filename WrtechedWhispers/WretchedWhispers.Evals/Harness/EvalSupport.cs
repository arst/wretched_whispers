using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using WretchedWhispers.Api.Models;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// Shared skeleton for live eval scenarios: wiring an <see cref="IChatClient"/> from Azure OpenAI
/// settings (or null when unconfigured, so [Fact]s can skip cleanly) and building the disk-backed
/// reporting configuration with response caching. Scenario suites (e.g. CampaignCreationEvals,
/// DomainAuthorityEvals) supply their own evaluator list and execution name.
/// </summary>
public static class EvalSupport
{
    // DiskBasedReportingConfiguration.Create signature (verified against 10.6.0):
    //   Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching,
    //          timeToLiveForCacheEntries, cachingKeys, executionName,
    //          evaluationMetricInterpreter, tags)
    // The plan omitted timeToLiveForCacheEntries (nullable TimeSpan) — we pass null to use the default.
    // ChatConfiguration is not IAsyncDisposable; no await using needed.
    public static ReportingConfiguration CreateReportingConfiguration(
        IChatClient chatClient, IEnumerable<IEvaluator> evaluators, string executionName)
    {
        var chatConfiguration = new ChatConfiguration(chatClient);
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: Path.Combine(AppContext.BaseDirectory, ".eval-results"),
            evaluators: evaluators,
            chatConfiguration: chatConfiguration,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: null,
            executionName: executionName);
    }

    // Mirrors AgentConfiguration's Azure wiring. Values can come from appsettings, user secrets, or
    // environment variables. Returns null if any are absent so the [Fact] skips cleanly.
    public static IChatClient? TryCreateAzureChatClient()
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
            .AddUserSecrets(typeof(EvalSupport).Assembly, optional: true)
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
}
