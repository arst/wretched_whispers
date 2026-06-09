using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// Builds production wiring (real Core services + tools) over an in-memory SQLite database for an eval
/// run, seeded with an empty campaign + chat session so turns start in the CharacterCreation stage. The
/// supplied chat client is the one the agent will call.
/// </summary>
public sealed class EvalHost : IAsyncDisposable
{
    private const string TestUserId = "eval-user";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IChatClient _chatClient;
    private readonly List<EvalTurnRunner> _runners = new();

    public Guid SessionId { get; }
    public Guid ChatSessionId { get; }

    private EvalHost(SqliteConnection connection, ServiceProvider provider, IChatClient chatClient, Guid sessionId, Guid chatSessionId)
    {
        _connection = connection;
        _provider = provider;
        _chatClient = chatClient;
        SessionId = sessionId;
        ChatSessionId = chatSessionId;
    }

    public static async Task<EvalHost> CreateAsync(IChatClient chatClient)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite(connection));
        services.AddDomainServices();

        var provider = services.BuildServiceProvider();

        // Ensure schema exists using a short-lived scope.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Seed an empty campaign + chat session so the first turn starts in CharacterCreation stage.
        Guid sessionId;
        Guid chatSessionId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            SetTenantUser(sp);

            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
            var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();

            var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Eval Campaign", "A doomed eval run");
            await campaignsRepo.SaveCampaign(campaign, TestUserId);
            chatSessionId = await chatRepo.CreateSession(campaign.Id);

            sessionId = campaign.Id;
        }

        return new EvalHost(connection, provider, chatClient, sessionId, chatSessionId);
    }

    /// <summary>
    /// Creates a new <see cref="EvalTurnRunner"/> scoped to one eval turn and registers it for
    /// disposal. The runner is owned by this host and will be disposed when the host is disposed —
    /// callers must NOT dispose the runner themselves.
    /// </summary>
    public EvalTurnRunner CreateTurnRunner()
    {
        var scope = _provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetTenantUser(sp);

        var contextLoader = new SessionContextLoader(
            sp.GetRequiredService<ICampaignsRepository>(),
            sp.GetRequiredService<ICharactersRepository>(),
            sp.GetRequiredService<IEncountersRepository>(),
            NullLogger<SessionContextLoader>.Instance);

        var toolProvider = new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance);

        var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();
        var executor = new AgentExecutor(
            _chatClient,
            chatRepo,
            new ChatHistoryReducer(_chatClient, NullLogger<ChatHistoryReducer>.Instance),
            new PromptComposer(),
            NullLogger<AgentExecutor>.Instance);

        var runner = new EvalTurnRunner(scope, contextLoader, toolProvider, executor, chatRepo, SessionId, ChatSessionId);
        _runners.Add(runner);
        return runner;
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose runners (and their scopes) first, then the root provider, then the connection.
        foreach (var runner in _runners)
            await runner.DisposeAsync();

        await _provider.DisposeAsync();
        _connection.Close();
        _connection.Dispose();
    }

    private static void SetTenantUser(IServiceProvider sp) =>
        ((TenantContext)sp.GetRequiredService<ITenantContext>()).SetUserId(TestUserId);
}
