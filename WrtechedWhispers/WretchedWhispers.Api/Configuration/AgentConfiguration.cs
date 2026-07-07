using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;

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

        var timeoutSeconds = configuration.GetValue("GameSession:ResponseTimeoutSeconds", 180);
        var maxRetryAttempts = configuration.GetValue("GameSession:MaxRetryAttempts", 2);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Provider selection. Hosted web build leaves "Llm:Provider" unset → Azure (unchanged). The
        // desktop build sets it to "openai" so users bring their own key.
        //
        // Transient-fault resilience lives HERE, at the transport: the client retries an individual
        // model HTTP request (on 408/429/5xx/network errors) with exponential backoff, and bounds each
        // request with NetworkTimeout. This is deliberately NOT done around the agent run — that loop
        // executes state-mutating tools inside the turn's transaction, so retrying it as a whole would
        // double-apply tools. Retrying a single request never re-runs tools whose results are already
        // in the conversation. (See AgentExecutor.)
        var provider = configuration["Llm:Provider"];
        if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            // Desktop: the user's key is entered at runtime and can change, so the client rebuilds lazily.
            services.AddSingleton(_ => new DesktopLlmOptions(
                configuration["Llm:ApiKey"] ?? "", configuration["Llm:Model"] ?? "gpt-4o",
                configuration["Llm:BaseUrl"] ?? ""));
            services.AddSingleton<IChatClient>(sp =>
                new ReloadableOpenAIChatClient(sp.GetRequiredService<DesktopLlmOptions>(), timeout, maxRetryAttempts));
        }
        else
        {
            // Hosted: Azure OpenAI chat client (Microsoft.Extensions.AI). ChatClientAgent enables
            // automatic function invocation over this client.
            services.AddSingleton<IChatClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
                var clientOptions = new AzureOpenAIClientOptions
                {
                    NetworkTimeout = timeout,
                    RetryPolicy = new ClientRetryPolicy(maxRetries: maxRetryAttempts)
                };
                return new AzureOpenAIClient(
                        new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey), clientOptions)
                    .GetChatClient(settings.ChatModelDeployment)
                    .AsIChatClient();
            });
        }

        // Orchestration services
        services.AddScoped<TurnCoordinator>();
        services.AddScoped<ISessionContextLoader, SessionContextLoader>();
        services.AddScoped<IAgentToolProvider, AgentToolProvider>();
        services.AddScoped<IAgentExecutor, AgentExecutor>();
        services.AddScoped<ChatHistoryReducer>();
        services.AddScoped<PromptComposer>();

        // The stage-scoped game-tool classes (CharacterTools/CampaignTools/EncounterTools/DiceTools)
        // are constructed per turn inside AgentToolProvider — each needs the turn's SessionContext —
        // so they are not registered here. The Core services they depend on are registered with the
        // domain/infrastructure DI.

        return services;
    }
}
