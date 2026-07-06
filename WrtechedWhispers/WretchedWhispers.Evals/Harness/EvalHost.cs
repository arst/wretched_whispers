using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
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

            var campaign = Campaign.Create(Difficulty.Grim, "Eval Campaign", "A doomed eval run");
            await campaignsRepo.SaveCampaign(campaign, TestUserId);
            chatSessionId = await chatRepo.CreateSession(campaign.Id);

            sessionId = campaign.Id;
        }

        return new EvalHost(connection, provider, chatClient, sessionId, chatSessionId);
    }

    public static async Task<EvalHost> CreateCombatAsync(IChatClient chatClient)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetTenantUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();
        var encountersRepo = sp.GetRequiredService<IEncountersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        var abilities = new Abilities(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(0),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 120,
            FoodDays: 3,
            Container: "satchel",
            Gear1: null,
            Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Staff),
            Armor: new Armor(ArmorTier.Medium),
            Shield: null,
            Scrolls: []);
        var character = Character.Create(Guid.NewGuid(), "Tuck", 2, abilities, equipment, dice);
        await charactersRepo.Save(character);

        var encounter = Encounter.Create("Chapel Duel", "A priest blocks the road.", EncounterType.Hostile, dice);
        encounter.AddAdversary(new Adversary(
            "Priest",
            new HitPoints(4, 4),
            new Armor(ArmorTier.Light),
            7,
            new AttackProfile("Brass crucible", DiceExpr.Parse("1d4"))));
        encounter.StartEncounter();
        await encountersRepo.Save(encounter);

        campaign.JoinGame(character.Id);
        campaign.AddEncounter(encounter.Id);
        campaign.Start();
        await campaignsRepo.SaveCampaign(campaign, TestUserId);

        return host;
    }

    /// <summary>
    /// Same seed as <see cref="CreateCombatAsync"/> (character joined, campaign started) but with no
    /// encounter attached, so <c>DeriveStage</c> falls through to <see cref="SessionStage.Exploration"/>.
    /// </summary>
    public static async Task<EvalHost> CreateExplorationAsync(IChatClient chatClient)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetTenantUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        var abilities = new Abilities(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(0),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 120,
            FoodDays: 3,
            Container: "satchel",
            Gear1: null,
            Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Staff),
            Armor: new Armor(ArmorTier.Medium),
            Shield: null,
            // One scroll in the pack so casting is actually available — the CastScroll eval needs a real
            // scroll to cast, and the GM correctly refuses to cast one the character does not possess.
            Scrolls: [new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, "Palms Open the Southern Gate")]);
        var character = Character.Create(Guid.NewGuid(), "Tuck", 2, abilities, equipment, dice);
        await charactersRepo.Save(character);

        campaign.JoinGame(character.Id);
        campaign.Start();
        await campaignsRepo.SaveCampaign(campaign, TestUserId);

        return host;
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
            new ChatHistoryReducer(_chatClient, chatRepo, NullLogger<ChatHistoryReducer>.Instance),
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
