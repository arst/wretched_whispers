using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Resilience;
using Polly;
using Polly.Retry;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Plugins.CombatAgent;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Configuration;

public static class AgentConfiguration
{
    public static IServiceCollection AddGameAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Singleton concurrency guard for per-session 409 Conflict
        services.AddSingleton<SessionConcurrencyGuard>();

        // Bind AzureOpenAI settings — section name matches Settings.AzureOpenAiSettings class name
        services.Configure<AzureOpenAiSettings>(configuration.GetSection("AzureOpenAiSettings"));

        // Azure OpenAI chat client (Microsoft.Extensions.AI). ChatClientAgent enables automatic
        // function invocation over this client.
        services.AddSingleton<IChatClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
            return new AzureOpenAIClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey))
                .GetChatClient(settings.ChatModelDeployment)
                .AsIChatClient();
        });

        // Orchestration services
        services.AddScoped<TurnCoordinator>();
        services.AddScoped<ISessionContextLoader, SessionContextLoader>();
        services.AddScoped<IAgentToolProvider, AgentToolProvider>();
        services.AddScoped<IAgentExecutor, AgentExecutor>();
        services.AddScoped<ICombatAgentService, CombatAgentService>();
        services.AddScoped<PromptComposer>();

        // Game plugins as Scoped so they resolve request-scoped DbContext/repos.
        // Used as inner services for the stage-scoped wrapper plugins.
        services.AddScoped<CharacterPlugin>();
        services.AddScoped<CampaignPlugin>();
        services.AddScoped<EncounterPlugin>();
        services.AddScoped<DicePlugin>();

        // Resilience pipeline for LLM retry with exponential backoff
        var timeoutSeconds = configuration.GetValue("GameSession:ResponseTimeoutSeconds", 180);

        services.AddResiliencePipeline("llm-retry", pipelineBuilder =>
        {
            pipelineBuilder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = configuration.GetValue("GameSession:MaxRetryAttempts", 2),
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                })
                .AddTimeout(TimeSpan.FromSeconds(timeoutSeconds));
        });

        return services;
    }
}
