using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;

namespace WretchedWhispers.Engine.Configuration;

public static class AgentConfiguration
{
    public static IServiceCollection AddGameAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var timeoutSeconds = configuration.GetValue("GameSession:ResponseTimeoutSeconds", 180);
        var maxRetryAttempts = configuration.GetValue("GameSession:MaxRetryAttempts", 2);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Provider selection. Hosted web build leaves "Llm:Provider" unset → Azure (unchanged). The
        // standalone profiles set it to "openai" so users bring their own key.
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
            // Standalone: the user's key is entered at runtime and can change, so the client rebuilds lazily.
            services.AddSingleton(_ => new DesktopLlmOptions(
                configuration["Llm:ApiKey"] ?? "", configuration["Llm:Model"] ?? "gpt-4o",
                configuration["Llm:BaseUrl"] ?? ""));
            services.AddSingleton<IChatClient>(sp =>
                new ReloadableOpenAIChatClient(sp.GetRequiredService<DesktopLlmOptions>(), timeout, maxRetryAttempts));
        }
        else
        {
            // Bind AzureOpenAI settings — section name matches the AzureOpenAiSettings class name.
            // Validated on first use (NOT ValidateOnStart: the checked-in appsettings ships empty
            // values, and a keyless `dotnet run` must still boot). A misconfigured endpoint now
            // fails the first turn with these messages instead of a raw UriFormatException.
            services.AddOptions<AzureOpenAiSettings>()
                .Bind(configuration.GetSection("AzureOpenAiSettings"))
                .Validate(s => Uri.TryCreate(s.Endpoint, UriKind.Absolute, out _),
                    "AzureOpenAiSettings:Endpoint must be an absolute URL")
                .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey),
                    "AzureOpenAiSettings:ApiKey is required")
                .Validate(s => !string.IsNullOrWhiteSpace(s.ChatModelDeployment),
                    "AzureOpenAiSettings:ChatModelDeployment is required");

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

        return services.AddGameAgentOrchestration();
    }

    /// <summary>
    /// The orchestration pipeline minus the <see cref="IChatClient"/>. Split out so the eval harness
    /// can register its own client (scripted or caching-wrapped) and still resolve the exact pipeline
    /// the product ships — hand-copied wiring there would silently drift from this list.
    /// </summary>
    public static IServiceCollection AddGameAgentOrchestration(this IServiceCollection services)
    {
        services.AddScoped<TurnCoordinator>();
        services.AddHostedService<TurnWorker>();
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
