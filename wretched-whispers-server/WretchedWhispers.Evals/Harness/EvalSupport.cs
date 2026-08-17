using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using WretchedWhispers.Engine.Models;
using Xunit;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// Shared skeleton for live eval scenarios: wiring an <see cref="IChatClient"/> from Azure OpenAI
/// settings (skipping the test when unconfigured) and building the disk-backed reporting
/// configuration with response caching. Scenario suites supply their evaluator list per scenario via
/// <see cref="EvalScenario.StartAsync"/>.
/// </summary>
public static class EvalSupport
{
    /// <summary>Groundedness scores at or above this bar pass; below it the metric (and the test) fail.</summary>
    public const double GroundednessPassBar = 4;

    /// <summary>
    /// One execution = one test-process run, so the reporting store accumulates comparable executions
    /// over time instead of piling every run into a single fixed bucket. CI runs are named by commit;
    /// local runs by timestamp.
    /// </summary>
    private static readonly string ExecutionName =
        Environment.GetEnvironmentVariable("GITHUB_SHA") is { Length: >= 8 } sha
            ? sha[..8]
            : $"local-{DateTime.Now:yyyyMMdd-HHmmss}";

    /// <summary>
    /// Results + response cache live under the solution root (gitignored), not bin/ — so they survive
    /// `dotnet clean` and `aieval report` has a stable path to point at. Override with WW_EVAL_RESULTS.
    /// </summary>
    private static string ResolveStorageRoot()
    {
        if (Environment.GetEnvironmentVariable("WW_EVAL_RESULTS") is { Length: > 0 } overridePath)
            return overridePath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WretchedWhispers.slnx")))
            dir = dir.Parent;

        return Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, ".eval-results");
    }

    // DiskBasedReportingConfiguration.Create signature (verified against 10.6.0):
    //   Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching,
    //          timeToLiveForCacheEntries, cachingKeys, executionName,
    //          evaluationMetricInterpreter, tags)
    // ChatConfiguration is not IAsyncDisposable; no await using needed.
    public static ReportingConfiguration CreateReportingConfiguration(
        IChatClient chatClient, IEnumerable<IEvaluator> evaluators, string suite)
    {
        var chatConfiguration = new ChatConfiguration(chatClient);
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: ResolveStorageRoot(),
            evaluators: evaluators,
            chatConfiguration: chatConfiguration,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: null,
            executionName: ExecutionName,
            evaluationMetricInterpreter: InterpretMetric,
            tags: [suite]);
    }

    /// <summary>
    /// Puts the pass/fail bar INTO the stored metrics, so a generated report shows red/green instead
    /// of bare numbers — the same thresholds the xUnit asserts enforce.
    /// </summary>
    private static EvaluationMetricInterpretation? InterpretMetric(EvaluationMetric metric) =>
        metric switch
        {
            BooleanMetric b => b.Value switch
            {
                true => new EvaluationMetricInterpretation(EvaluationRating.Good, failed: false, reason: null),
                false => new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason: null),
                null => new EvaluationMetricInterpretation(
                    EvaluationRating.Inconclusive, failed: true, reason: "Metric has no value."),
            },
            NumericMetric { Value: { } v } when metric.Name == GroundednessEvaluator.GroundednessMetricName =>
                v >= GroundednessPassBar
                    ? new EvaluationMetricInterpretation(EvaluationRating.Good, failed: false, reason: null)
                    : new EvaluationMetricInterpretation(
                        EvaluationRating.Unacceptable, failed: true, reason: $"Below the {GroundednessPassBar} bar."),
            _ => null,
        };

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

/// <summary>
/// One live eval scenario, ceremony included: skip-when-unconfigured, reporting configuration,
/// scenario run, and the caching-wrapped chat client for the game under test.
/// </summary>
public sealed class EvalScenario : IAsyncDisposable
{
    public ScenarioRun Run { get; }

    /// <summary>The caching-wrapped client — pass this to <see cref="EvalHost"/> so the game turn's
    /// model responses are cached alongside the judge's.</summary>
    public IChatClient ChatClient { get; }

    private EvalScenario(ScenarioRun run, IChatClient chatClient)
    {
        Run = run;
        ChatClient = chatClient;
    }

    /// <summary>
    /// Skips the calling test when Azure OpenAI is not configured; otherwise starts a scenario run
    /// with the given evaluators, tagged with <paramref name="suite"/>.
    /// <paramref name="stripSamplingOptions"/>: GroundednessEvaluator hardcodes Temperature = 0 and a
    /// MaxOutputTokens cap on its judge request. Reasoning-model deployments (e.g. gpt-5-mini) reject
    /// any non-default temperature, and burn the token cap on hidden reasoning — returning an EMPTY
    /// judge response that fails score parsing — so strip both back to null (provider defaults).
    /// NOTE: this wraps the scenario's ONE shared client, so it applies to the game-under-test turn as
    /// well as the judge call — a no-op there today (AgentExecutor never sets either), but rescope if
    /// a future scenario's game path relies on them.
    /// </summary>
    public static async Task<EvalScenario> StartAsync(
        string suite,
        string scenarioName,
        IEnumerable<IEvaluator> evaluators,
        bool stripSamplingOptions = false)
    {
        var chatClient = EvalSupport.TryCreateAzureChatClient();
        Assert.SkipWhen(chatClient is null, "Azure OpenAI credentials not configured; skipping live eval.");

        if (stripSamplingOptions)
            chatClient = chatClient.AsBuilder().ConfigureOptions(o =>
            {
                o.Temperature = null;
                o.MaxOutputTokens = null;
            }).Build();

        var reporting = EvalSupport.CreateReportingConfiguration(chatClient, evaluators, suite);
        var run = await reporting.CreateScenarioRunAsync(scenarioName);

        var chatConfiguration = run.ChatConfiguration
            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
        return new EvalScenario(run, chatConfiguration.ChatClient);
    }

    public ValueTask DisposeAsync() => Run.DisposeAsync();
}
