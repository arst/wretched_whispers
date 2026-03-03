using Microsoft.Extensions.Resilience;
using Polly;
using Polly.Retry;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Configuration;

public static class SemanticKernelConfiguration
{
    public static IServiceCollection AddSemanticKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Singleton concurrency guard for per-session 409 Conflict
        services.AddSingleton<SessionConcurrencyGuard>();

        // Scoped GameSessionService (builds Kernel per-turn internally)
        services.AddScoped<GameSessionService>();

        // Register SK plugins as Scoped so they resolve request-scoped DbContext/repos
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
