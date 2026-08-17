using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.Configuration;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Evals.Harness;

/// <summary>
/// Builds production wiring (real Core services + the shipped agent pipeline via
/// <see cref="AgentConfiguration.AddGameAgentOrchestration"/>) over an in-memory SQLite database for
/// an eval run, seeded with an empty campaign + chat session so turns start in the CharacterCreation
/// stage. The supplied chat client is registered as THE <see cref="IChatClient"/> — the same slot the
/// product fills with Azure/OpenAI.
/// </summary>
public sealed class EvalHost : IAsyncDisposable
{
    private const string TestUserId = "eval-user";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly List<EvalTurnRunner> _runners = new();

    public Guid SessionId { get; }
    public Guid ChatSessionId { get; }

    private EvalHost(SqliteConnection connection, ServiceProvider provider, Guid sessionId, Guid chatSessionId)
    {
        _connection = connection;
        _provider = provider;
        SessionId = sessionId;
        ChatSessionId = chatSessionId;
    }

    public static async Task<EvalHost> CreateAsync(IChatClient chatClient)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite(connection));
        services.AddDomainServices();
        services.AddGameAgentOrchestration();
        services.AddSingleton(chatClient);
        // Deterministic dice (last registration wins over AddDomainServices' unseeded default):
        // rolled values land in tool results, which land in model requests — unseeded dice would
        // change the request hash every run and defeat the response cache.
        services.AddSingleton<IRandomService>(new SeededRandomService(seed: 1));

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
            SetEvalUser(sp);

            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
            var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();

            var campaign = Campaign.Create(Difficulty.Grim, "Eval Campaign", "A doomed eval run");
            await campaignsRepo.SaveCampaign(campaign);
            chatSessionId = await chatRepo.CreateSession(campaign.Id);

            sessionId = campaign.Id;
        }

        return new EvalHost(connection, provider, sessionId, chatSessionId);
    }

    /// <summary>Combat mid-fight: the priest stands, the encounter is started. Pass
    /// <paramref name="scrolls"/> for scenarios that cast in combat, <paramref name="omens"/> for
    /// scenarios that spend omens (defaults keep the shared seed byte-identical so existing
    /// scenarios' cached responses stay valid — Tuck rolls with 0 omens).</summary>
    public static async Task<EvalHost> CreateCombatAsync(
        IChatClient chatClient, IReadOnlyList<Scroll>? scrolls = null, int omens = 0)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetEvalUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();
        var encountersRepo = sp.GetRequiredService<IEncountersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        var character = SeedTuck(dice, scrolls: scrolls?.ToList() ?? []);
        if (omens > 0) character.Omens.Refill(omens);
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
        await campaignsRepo.SaveCampaign(campaign);

        return host;
    }

    /// <summary>
    /// The combat aftermath: same fight as <see cref="CreateCombatAsync"/> but the priest already
    /// slain and the encounter ended (not resolved), so <c>DeriveStage</c> gives
    /// <see cref="SessionStage.Resolution"/> and the Resolution-only tools are in play.
    /// </summary>
    public static async Task<EvalHost> CreateResolutionAsync(IChatClient chatClient)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetEvalUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();
        var encountersRepo = sp.GetRequiredService<IEncountersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        var character = SeedTuck(dice, scrolls: []);
        await charactersRepo.Save(character);

        var encounter = Encounter.Create("Chapel Duel", "A priest blocks the road.", EncounterType.Hostile, dice);
        var priest = new Adversary(
            "Priest",
            new HitPoints(4, 4),
            new Armor(ArmorTier.Light),
            7,
            new AttackProfile("Brass crucible", DiceExpr.Parse("1d4")));
        encounter.AddAdversary(priest);
        encounter.StartEncounter();
        priest.ReceiveDamage(4);
        encounter.EndEncounter();
        await encountersRepo.Save(encounter);

        campaign.JoinGame(character.Id);
        campaign.AddEncounter(encounter.Id);
        campaign.Start();
        await campaignsRepo.SaveCampaign(campaign);

        return host;
    }

    /// <summary>
    /// A fight the character can neither win nor outlast: the Bell-Warden has 30 HP behind heavy
    /// armor (Tuck's staff barely scratches it), morale 12 (2d6 can never exceed it, so it never
    /// breaks and never flees), and swings 2d6 against Tuck's 2 HP. With the seeded dice the only
    /// possible ending is the player's death — the deterministic stage for death-authority scenarios.
    /// </summary>
    public static async Task<EvalHost> CreateDeathFightAsync(IChatClient chatClient)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetEvalUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();
        var encountersRepo = sp.GetRequiredService<IEncountersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        var character = SeedTuck(dice, scrolls: []);
        await charactersRepo.Save(character);

        var encounter = Encounter.Create(
            "The Bell-Warden",
            "A towering warden of the sunken chapel swings its cracked iron bell.",
            EncounterType.Hostile, dice);
        encounter.AddAdversary(new Adversary(
            "Bell-Warden",
            new HitPoints(30, 30),
            new Armor(ArmorTier.Heavy),
            12,
            new AttackProfile("Cracked iron bell", DiceExpr.Parse("2d6"))));
        encounter.StartEncounter();
        await encountersRepo.Save(encounter);

        campaign.JoinGame(character.Id);
        campaign.AddEncounter(encounter.Id);
        campaign.Start();
        await campaignsRepo.SaveCampaign(campaign);

        return host;
    }

    /// <summary>
    /// The state a brand-new session is in the moment the create-session form has been submitted: the
    /// character is rolled and joined, the campaign is NOT yet configured, so <c>DeriveStage</c> gives
    /// <see cref="SessionStage.CampaignSetup"/> and the next turn is the opening narration. Mirrors
    /// <c>SessionEndpoints.CreateSession</c>, and rolls through the real CharacterCreationService so the
    /// class actually shapes the wretch.
    /// </summary>
    public static async Task<EvalHost> CreateOpeningAsync(
        IChatClient chatClient, string characterName, CharacterClass characterClass)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetEvalUser(sp);

        var creation = sp.GetRequiredService<CharacterCreationService>();
        var campaignService = sp.GetRequiredService<CampaignService>();

        var character = await creation.Create(characterName, Difficulty.Grim, characterClass);
        await campaignService.JoinCampaign(host.SessionId, character.Id);

        return host;
    }

    /// <summary>
    /// Same seed as <see cref="CreateCombatAsync"/> (character joined, campaign started) but with no
    /// encounter attached, so <c>DeriveStage</c> falls through to <see cref="SessionStage.Exploration"/>.
    /// Pass <paramref name="extraGear"/> to seed inventory items a scenario needs, <paramref name="omens"/>
    /// for scenarios that spend omens (defaults keep the shared seed byte-identical so existing
    /// scenarios' cached responses stay valid — Tuck rolls with 0 omens).
    /// </summary>
    public static async Task<EvalHost> CreateExplorationAsync(
        IChatClient chatClient, IReadOnlyList<InventoryItem>? extraGear = null, int omens = 0)
    {
        var host = await CreateAsync(chatClient);

        await using var scope = host._provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        SetEvalUser(sp);

        var dice = sp.GetRequiredService<Dice>();
        var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
        var charactersRepo = sp.GetRequiredService<ICharactersRepository>();

        var campaign = await campaignsRepo.Get(host.SessionId)
            ?? throw new InvalidOperationException("Seed campaign was not found.");

        // One scroll in the pack so casting is actually available — the CastScroll eval needs a real
        // scroll to cast, and the GM correctly refuses to cast one the character does not possess.
        var character = SeedTuck(
            dice, scrolls: [new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, "Palms Open the Southern Gate")]);
        foreach (var item in extraGear ?? []) character.AddItem(item);
        if (omens > 0) character.Omens.Refill(omens);
        await charactersRepo.Save(character);

        campaign.JoinGame(character.Id);
        campaign.Start();
        await campaignsRepo.SaveCampaign(campaign);

        return host;
    }

    /// <summary>The shared "Tuck" seed for combat/exploration scenarios. Keep the values stable —
    /// they feed the model prompt, and changing them invalidates every scenario's cached responses.</summary>
    private static Character SeedTuck(Dice dice, List<Scroll> scrolls)
    {
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
            Scrolls: scrolls);
        return Character.Create(Guid.NewGuid(), "Tuck", 2, abilities, equipment, dice);
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
        SetEvalUser(sp);

        var runner = new EvalTurnRunner(
            scope,
            sp.GetRequiredService<ISessionContextLoader>(),
            sp.GetRequiredService<IAgentToolProvider>(),
            sp.GetRequiredService<IAgentExecutor>(),
            sp.GetRequiredService<IChatHistoryRepository>(),
            SessionId,
            ChatSessionId);
        _runners.Add(runner);
        return runner;
    }

    /// <summary>Reads committed domain state after a turn (e.g. the active encounter's reaction, the
    /// derived stage) so scenarios can assert on the DOMAIN's truth, not on tool-result strings.</summary>
    public async Task<T> QueryAsync<T>(Func<IServiceProvider, Task<T>> query)
    {
        await using var scope = _provider.CreateAsyncScope();
        SetEvalUser(scope.ServiceProvider);
        return await query(scope.ServiceProvider);
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

    private static void SetEvalUser(IServiceProvider sp) =>
        ((UserContext)sp.GetRequiredService<IUserContext>()).SetUserId(TestUserId);
}
