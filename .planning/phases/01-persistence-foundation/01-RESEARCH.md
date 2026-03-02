# Phase 1: Persistence Foundation - Research

**Researched:** 2026-03-02
**Domain:** EF Core + SQLite persistence for DDD aggregates and SemanticKernel ChatHistory
**Confidence:** HIGH

## Summary

This phase replaces in-memory `ConcurrentDictionary` repositories with SQLite persistence using EF Core. The project targets .NET 9 and uses SemanticKernel 1.65.0. The domain model contains three aggregates (Character, Campaign, Encounter) with rich value object hierarchies including abstract types (ArmorTier), singleton instances, private constructors, and mutable state -- all of which must round-trip through serialization.

The recommended approach is a **value converter pattern** where each aggregate is stored as `(Id GUID PK, Data TEXT)` with `System.Text.Json` serialization, rather than EF Core's `ToJson()` owned entity mapping. The `ToJson()` approach would require modeling every nested type as an EF Core owned entity, which is impractical given the domain's abstract class hierarchies (ArmorTier -> HeavyArmorTier, etc.) and singleton patterns. The value converter approach gives full control over serialization via `JsonSerializerOptions` with polymorphic type discriminators.

Chat history uses a relational row-per-message schema as decided by the user, storing individual `ChatMessageContent` items serialized via `System.Text.Json` with SK's built-in polymorphic support for content types (TextContent, FunctionCallContent, FunctionResultContent).

**Primary recommendation:** Use EF Core 9.x with SQLite provider, value converters for aggregate JSON blobs, relational tables for chat messages, code-first migrations auto-applied on startup.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Single JSON blob per aggregate: schema is `(Id GUID PK, Data JSON)`
- All value objects, collections, and status flags serialized into one JSON column
- Consistent pattern across all three aggregates (Character, Campaign, Encounter)
- No separate tables for nested objects -- Inventory items, Scrolls, Abilities, HitPoints, status flags all live in the JSON blob
- EF Core 8+ JSON column support for mapping
- Chat history stored as individual message rows, not a JSON blob per session
- Each row stores: Id, SessionId, Role, Content, Timestamp, AuthorName
- Full SemanticKernel metadata persisted -- including tool call info and function results
- Enables future message history display and scrollback
- New `IChatHistoryRepository` interface defined in WretchedWhispers.Semantic project (not Core -- ChatHistory is an SK type)
- Session-based grouping: Session entity (SessionId, CampaignId, StartedAt) groups messages
- Single SQLite file for everything (aggregates + chat history + sessions)
- File lives in working directory: `./wretched-whispers.db`
- Path configurable via `appsettings.json` Database section (overridable by env var for Coolify deployment)
- Self-contained deployment target -- Coolify with volume mount at app root
- EF Core code-first migrations checked into source control
- Migrations auto-apply on application startup (zero-touch deployment)
- Replace `AddInMemoryInfrastructure()` entirely -- no parallel in-memory option
- Both SingleAgent.Console and Orchestration.Console switch to SQLite
- Tests use SQLite in-memory mode (`:memory:`) instead of the old `ConcurrentDictionary` repositories
- Database connection string configured via `appsettings.json` section, following the existing Settings pattern

### Claude's Discretion
- Exact EF Core entity configuration details
- JSON serialization settings (System.Text.Json options)
- Migration naming conventions
- DbContext internal organization
- Error handling for corrupt or missing database files

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INFR-02 | SQLite persistence for all game state (character, chat history, world state) | Full stack identified: EF Core 9.x + SQLite provider + value converters for aggregates + relational tables for chat history. Code patterns documented for all three aggregate types plus ChatHistory serialization. |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.x (latest 9.0.y) | SQLite database provider for EF Core | Official Microsoft provider; matches project's net9.0 TFM; includes JSON function support shipped in EF Core 8+ |
| Microsoft.EntityFrameworkCore.Design | 9.0.x | Migration tooling (design-time) | Required for `dotnet ef migrations add`; installed as dev dependency |
| Microsoft.EntityFrameworkCore | 9.0.x | Core ORM framework | Pulled transitively by Sqlite package; listed for clarity |
| System.Text.Json | (included in net9.0) | JSON serialization for aggregate blobs and ChatMessageContent | Built into .NET 9; supports polymorphic serialization via `[JsonDerivedType]` and `JsonSerializerOptions`; SK already depends on it |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.EntityFrameworkCore.Tools | 9.0.x | `dotnet ef` CLI tool | Migration creation and management; install as global tool or DotNetCliToolReference |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| EF Core value converter (JSON blob) | EF Core `ToJson()` owned entities | `ToJson()` enables LINQ queries into JSON but requires every nested type to be modeled as an owned entity. Impractical here due to abstract `ArmorTier` hierarchy with singleton instances, private constructors throughout, and deep nesting. Value converter gives full control over serialization. |
| SQLite | LiteDB | LiteDB is a document database with native BSON storage. Would simplify aggregate persistence but add a dependency and wouldn't support the relational chat history schema the user decided on. |
| Manual System.Text.Json | Newtonsoft.Json | STJ is the modern .NET standard, already used by SemanticKernel internally. No reason to add Newtonsoft. |

**Installation:**
```bash
# In Infrastructure project
dotnet add WretchedWhispers.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite --version 9.0.*
dotnet add WretchedWhispers.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 9.0.*

# In Semantic project (for IChatHistoryRepository interface -- only Abstractions needed)
# The Semantic project already references Microsoft.SemanticKernel which provides ChatHistory types

# Global tool for migrations
dotnet tool install --global dotnet-ef
```

## Architecture Patterns

### Recommended Project Structure
```
WretchedWhispers.Infrastructure/
├── Persistence/
│   ├── WretchedWhispersDbContext.cs        # Single DbContext for all entities
│   ├── Configurations/
│   │   ├── CharacterEntityConfiguration.cs  # EF entity config for Character wrapper
│   │   ├── CampaignEntityConfiguration.cs   # EF entity config for Campaign wrapper
│   │   ├── EncounterEntityConfiguration.cs  # EF entity config for Encounter wrapper
│   │   ├── ChatSessionConfiguration.cs      # EF entity config for Session
│   │   └── ChatMessageConfiguration.cs      # EF entity config for ChatMessage
│   ├── Entities/
│   │   ├── CharacterEntity.cs               # Thin wrapper: Id + Data (JSON string)
│   │   ├── CampaignEntity.cs                # Thin wrapper: Id + Data (JSON string)
│   │   ├── EncounterEntity.cs               # Thin wrapper: Id + Data (JSON string)
│   │   ├── ChatSessionEntity.cs             # Session: Id, CampaignId, StartedAt
│   │   └── ChatMessageEntity.cs             # Message: Id, SessionId, Role, Content, etc.
│   ├── Repositories/
│   │   ├── SqliteCharactersRepository.cs    # Implements ICharactersRepository
│   │   ├── SqliteCampaignsRepository.cs     # Implements ICampaignsRepository
│   │   ├── SqliteEncountersRepository.cs    # Implements IEncountersRepository
│   │   └── SqliteChatHistoryRepository.cs   # Implements IChatHistoryRepository
│   ├── Serialization/
│   │   └── AggregateJsonOptions.cs          # Shared JsonSerializerOptions with polymorphic config
│   └── Migrations/                          # EF Core auto-generated migrations
├── ServiceCollectionExtensions.cs           # Updated: AddSqliteInfrastructure() replaces AddInMemoryInfrastructure()
├── Settings.cs                              # Extended: DatabaseSettings section added
└── ...existing files (SeededRandomService, etc.)

WretchedWhispers.Semantic/
├── IChatHistoryRepository.cs                # New interface (ChatHistory is an SK type)
└── ...existing plugins
```

### Pattern 1: Aggregate as JSON Blob (Value Converter)
**What:** Each domain aggregate (Character, Campaign, Encounter) is stored in a table with just `Id` (GUID PK) and `Data` (TEXT column containing JSON). A thin "entity" wrapper class is what EF Core maps. The repository serializes/deserializes the domain aggregate to/from JSON.
**When to use:** For all three aggregate types -- consistent pattern as decided by user.
**Example:**
```csharp
// Entity wrapper (what EF Core sees)
public class CharacterEntity
{
    public Guid Id { get; set; }
    public string Data { get; set; } = string.Empty;
}

// Repository implementation
public class SqliteCharactersRepository : ICharactersRepository
{
    private readonly WretchedWhispersDbContext _db;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteCharactersRepository(
        WretchedWhispersDbContext db,
        JsonSerializerOptions jsonOptions)
    {
        _db = db;
        _jsonOptions = jsonOptions;
    }

    public async Task<Character?> Get(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Characters.FindAsync([id], ct);
        if (entity is null) return null;
        return JsonSerializer.Deserialize<Character>(entity.Data, _jsonOptions);
    }

    public async Task Save(Character character, CancellationToken ct = default)
    {
        var entity = await _db.Characters.FindAsync([character.Id], ct);
        var json = JsonSerializer.Serialize(character, _jsonOptions);

        if (entity is null)
        {
            entity = new CharacterEntity { Id = character.Id, Data = json };
            _db.Characters.Add(entity);
        }
        else
        {
            entity.Data = json;
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

### Pattern 2: Chat History as Relational Rows
**What:** Chat messages stored individually in a relational table, grouped by session. Each message row stores the SK `ChatMessageContent` metadata needed for full-fidelity reconstruction.
**When to use:** For chat history persistence -- enables future scrollback and per-message access.
**Example:**
```csharp
// Entity for chat messages
public class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;      // "user", "assistant", "system", "tool"
    public string? Content { get; set; }                    // Text content (may be null for tool calls)
    public string? AuthorName { get; set; }
    public string? ItemsJson { get; set; }                  // Serialized Items collection (FunctionCallContent, etc.)
    public string? MetadataJson { get; set; }               // Additional SK metadata
    public DateTime Timestamp { get; set; }
    public int OrderIndex { get; set; }                     // Ordering within session

    public ChatSessionEntity Session { get; set; } = null!;
}

public class ChatSessionEntity
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public DateTime StartedAt { get; set; }

    public List<ChatMessageEntity> Messages { get; set; } = [];
}
```

### Pattern 3: DbContext with Auto-Migration
**What:** Single DbContext with `Database.Migrate()` called at startup for zero-touch deployment.
**When to use:** Application startup in both console apps.
**Example:**
```csharp
public class WretchedWhispersDbContext : DbContext
{
    public WretchedWhispersDbContext(DbContextOptions<WretchedWhispersDbContext> options)
        : base(options) { }

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CampaignEntity> Campaigns => Set<CampaignEntity>();
    public DbSet<EncounterEntity> Encounters => Set<EncounterEntity>();
    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WretchedWhispersDbContext).Assembly);
    }
}

// In ServiceCollectionExtensions -- new method
public static IServiceCollection AddSqliteInfrastructure(
    this IServiceCollection services,
    string connectionString)
{
    services.AddDbContext<WretchedWhispersDbContext>(options =>
        options.UseSqlite(connectionString));

    services.AddScoped<ICharactersRepository, SqliteCharactersRepository>();
    services.AddScoped<ICampaignsRepository, SqliteCampaignsRepository>();
    services.AddScoped<IEncountersRepository, SqliteEncountersRepository>();
    services.AddScoped<IChatHistoryRepository, SqliteChatHistoryRepository>();
    // Domain services remain as before
    services.AddSingleton<CharacterCreationService>();
    services.AddSingleton<CharacterService>();
    services.AddSingleton<EncounterService>();
    services.AddSingleton<CampaignService>();
    return services;
}

// In Program.cs -- startup migration
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
db.Database.Migrate();
```

### Pattern 4: SQLite In-Memory for Tests
**What:** Tests use SQLite `:memory:` mode with a shared, open connection to replace the old `ConcurrentDictionary` repositories.
**When to use:** All persistence tests.
**Example:**
```csharp
public class SqliteTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open(); // Must stay open for in-memory DB to persist

        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new WretchedWhispersDbContext(options);
        Context.Database.EnsureCreated();
    }

    public WretchedWhispersDbContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
```

### Anti-Patterns to Avoid
- **Mapping domain aggregates as EF entities directly:** Never let EF Core's change tracker manage the domain model. Use thin wrapper entities and serialize/deserialize in the repository. The domain model has private constructors, abstract hierarchies, and singleton instances that EF Core cannot handle natively.
- **Using EF Core InMemory provider for tests:** The InMemory provider does not behave like a real relational database. SQLite `:memory:` mode is strictly better for testing persistence code.
- **Keeping in-memory repositories as a parallel option:** The user explicitly decided to remove the in-memory path. One persistence strategy, cleanly wired.
- **Storing chat history as a single JSON blob per session:** The user explicitly chose row-per-message to enable future scrollback and display.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON polymorphic serialization (ArmorTier hierarchy) | Custom type discriminator logic | `System.Text.Json` `[JsonDerivedType]` attribute or `JsonSerializerOptions` with `JsonPolymorphismOptions` | Built-in since .NET 7; handles type discriminators automatically; well-tested |
| Database migrations | Manual SQL scripts or `EnsureCreated()` for production | EF Core Migrations with `Database.Migrate()` | Migrations track incremental schema changes; `EnsureCreated()` doesn't handle upgrades |
| ChatMessageContent serialization | Custom per-field extraction | `System.Text.Json` with SK's built-in `KernelContent` polymorphic support | SK already registers `[JsonDerivedType]` for TextContent, FunctionCallContent, FunctionResultContent, etc. |
| Connection string management | Hardcoded paths | `IConfiguration` binding with env var override | Standard .NET configuration; supports appsettings.json + env vars for Coolify |
| Change detection for JSON blobs | Custom dirty-flag tracking | Always overwrite on Save (the blob is cheap) | Aggregates are small (< 10KB); comparing JSON strings for changes adds complexity with no benefit |

**Key insight:** The domain model is designed with DDD patterns (private constructors, encapsulation, rich behavior) that fight against ORM direct mapping. The JSON blob approach respects the domain model's design by treating persistence as serialization, not relational mapping.

## Common Pitfalls

### Pitfall 1: System.Text.Json Cannot Deserialize Private Constructors by Default
**What goes wrong:** `JsonSerializer.Deserialize<Character>()` throws because Character has only a private constructor and properties with private setters.
**Why it happens:** System.Text.Json requires a public parameterless constructor or a `[JsonConstructor]`-annotated constructor by default.
**How to avoid:** Add `[JsonConstructor]` to the private constructor, or use `[JsonInclude]` on properties with private setters. Alternatively, configure a custom `DefaultJsonTypeInfoResolver` with modifiers that handle private members via reflection. The `[JsonConstructor]` approach is cleanest.
**Warning signs:** `NotSupportedException` or `JsonException` mentioning "deserialization of reference types without parameterless constructor is not supported."

### Pitfall 2: ArmorTier Abstract Hierarchy Serialization
**What goes wrong:** `ArmorTier` is abstract with four derived types (HeavyArmorTier, MediumArmorTier, LightArmorTier, NoArmorTier), each using singleton instances. Default serialization loses the concrete type, and deserialization can't instantiate an abstract class.
**Why it happens:** System.Text.Json needs polymorphic type information to round-trip abstract types.
**How to avoid:** Add `[JsonDerivedType(typeof(HeavyArmorTier), "heavy")]` etc. to the `ArmorTier` base class. For singletons, add a custom `JsonConverter<ArmorTier>` that maps to/from the singleton instances rather than creating new objects. Alternatively, serialize just the tier name string and reconstitute via a factory.
**Warning signs:** `NotSupportedException` mentioning "deserialization of interface or abstract types is not supported."

### Pitfall 3: SQLite In-Memory Connection Lifetime
**What goes wrong:** Test database disappears between operations because the connection was closed.
**Why it happens:** SQLite in-memory databases exist only while the connection is open. If EF Core opens and closes connections per-operation (default pooling behavior), the database is recreated empty each time.
**How to avoid:** Open the `SqliteConnection` explicitly before creating `DbContextOptions` and keep it open for the test's lifetime. Pass the already-open connection to `UseSqlite()`.
**Warning signs:** "table does not exist" errors in tests that worked a moment ago.

### Pitfall 4: Scoped DbContext vs Singleton Repositories
**What goes wrong:** Injecting a Scoped `DbContext` into Singleton services causes `ObjectDisposedException` or stale data.
**Why it happens:** The current in-memory repositories are registered as Singletons. Switching to Scoped repositories with a Scoped DbContext changes the lifetime model.
**How to avoid:** Register repositories as Scoped (matching DbContext lifetime). Update the console apps to create proper scopes. The `CharacterService`, `CampaignService` etc. that depend on repositories must also be Scoped or use `IServiceScopeFactory`.
**Warning signs:** `InvalidOperationException` about resolving scoped service from root provider.

### Pitfall 5: ChatMessageContent Items Serialization Complexity
**What goes wrong:** SK's `ChatMessageContent.Items` collection contains polymorphic types (TextContent, ImageContent, FunctionCallContent, FunctionResultContent). Default serialization loses type information; deserialization produces base `KernelContent` objects.
**Why it happens:** The `Items` collection is `IReadOnlyList<KernelContent>` where `KernelContent` has multiple derived types.
**How to avoid:** Use `JsonSerializer` with the `JsonSerializerOptions` that include SK's polymorphic type info. SK already registers `[JsonDerivedType]` attributes on `KernelContent`. Verify round-trip with test data containing function calls.
**Warning signs:** Deserialized messages have empty or wrong Items; function call history is lost.

### Pitfall 6: EF Core Migration Assembly Location
**What goes wrong:** `dotnet ef migrations add` fails with "Unable to create an object of type 'DbContext'" or puts migrations in the wrong project.
**Why it happens:** EF Core Design tools need to find the DbContext at design time. If the DbContext is in a class library (Infrastructure) but the startup project is the Console app, the migration tool needs to be told which is which.
**How to avoid:** Use `--startup-project` and `--project` flags: `dotnet ef migrations add InitialCreate --project WretchedWhispers.Infrastructure --startup-project WretchedWhispers.SingleAgent.Console`. Or add an `IDesignTimeDbContextFactory` in the Infrastructure project.
**Warning signs:** Migrations appear in the wrong project or migration commands fail at design time.

### Pitfall 7: Circular References in Domain Model
**What goes wrong:** Character -> Inventory -> InventoryItems and Campaign -> _encounters (Guid list, safe) / _characters (Guid list, safe). The `Encounter` contains `Adversary` objects which have `Armor` objects. Deeply nested serialization can hit unexpected issues.
**Why it happens:** Complex object graphs with back-references.
**How to avoid:** The current domain model uses Guid references between aggregates (Campaign stores `List<Guid>` for encounters and characters, not the objects themselves), which is already safe. Within an aggregate, verify there are no circular references. The `Character` aggregate graph is a tree (no cycles). Use `JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles` as a safety net if needed, but the current model should not require it.
**Warning signs:** `JsonException` about "a possible object cycle was detected."

## Code Examples

Verified patterns from official sources:

### System.Text.Json Polymorphic Serialization for ArmorTier
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism
[JsonDerivedType(typeof(HeavyArmorTier), "heavy")]
[JsonDerivedType(typeof(MediumArmorTier), "medium")]
[JsonDerivedType(typeof(LightArmorTier), "light")]
[JsonDerivedType(typeof(NoArmorTier), "none")]
public abstract class ArmorTier { ... }

// Custom converter to map to singleton instances
public class ArmorTierConverter : JsonConverter<ArmorTier>
{
    public override ArmorTier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Read the type discriminator to determine which singleton to return
        using var doc = JsonDocument.ParseValue(ref reader);
        var discriminator = doc.RootElement.GetProperty("$type").GetString();
        return discriminator switch
        {
            "heavy" => HeavyArmorTier.Instance,
            "medium" => MediumArmorTier.Instance,
            "light" => LightArmorTier.Instance,
            "none" => NoArmorTier.Instance,
            _ => throw new JsonException($"Unknown armor tier: {discriminator}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ArmorTier value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value switch
        {
            HeavyArmorTier => "heavy",
            MediumArmorTier => "medium",
            LightArmorTier => "light",
            NoArmorTier => "none",
            _ => throw new JsonException($"Unknown armor tier type: {value.GetType()}")
        });
        // Write properties
        writer.WriteNumber("defencePenalty", value.DefencePenalty);
        writer.WriteNumber("agilityPenalty", value.AgilityPenalty);
        writer.WriteEndObject();
    }
}
```

### JsonSerializerOptions Configuration
```csharp
// Shared options for aggregate serialization
public static class AggregateJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false, // compact storage
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new ArmorTierConverter(),
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
        return options;
    }
}
```

### EF Core Entity Configuration (Aggregate Wrapper)
```csharp
// Source: https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions
public class CharacterEntityConfiguration : IEntityTypeConfiguration<CharacterEntity>
{
    public void Configure(EntityTypeBuilder<CharacterEntity> builder)
    {
        builder.ToTable("Characters");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Data).IsRequired().HasColumnType("TEXT");
    }
}
```

### Settings Extension for Database Configuration
```csharp
// Follows existing Settings pattern in project
public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "Data Source=./wretched-whispers.db";
}

// In Settings class -- add alongside AzureOpenAiSettings
public DatabaseSettings Database => _database ??= GetSettings<DatabaseSettings>();
```

### IChatHistoryRepository Interface (in Semantic project)
```csharp
// Located in WretchedWhispers.Semantic -- because ChatHistory is an SK type
using Microsoft.SemanticKernel.ChatCompletion;

public interface IChatHistoryRepository
{
    Task<ChatHistory?> LoadSession(Guid sessionId, CancellationToken ct = default);
    Task SaveMessage(Guid sessionId, ChatMessageContent message, CancellationToken ct = default);
    Task<Guid> CreateSession(Guid campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetSessionsForCampaign(Guid campaignId, CancellationToken ct = default);
}
```

### Console App Startup with Migration
```csharp
// In Program.cs -- replace AddInMemoryInfrastructure() call
Settings settings = new();
// ...
void RegisterServices(IKernelBuilder builder)
{
    builder.AddAzureOpenAIChatCompletion(
        settings.AzureOpenAi.ChatModelDeployment,
        settings.AzureOpenAi.Endpoint,
        settings.AzureOpenAi.ApiKey);

    var dbSettings = settings.Database;
    builder.Services.AddSqliteInfrastructure(dbSettings.ConnectionString);
}

// After building the kernel, apply migrations
using var scope = services.BuildServiceProvider().CreateScope();
var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
db.Database.Migrate();
```

### SQLite In-Memory Test Base
```csharp
// Source: https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public abstract class SqliteTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected WretchedWhispersDbContext Db { get; }

    protected SqliteTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<WretchedWhispersDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new WretchedWhispersDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| EF Core InMemory provider for tests | SQLite `:memory:` for tests | EF Core team recommendation since EF Core 7 | InMemory provider doesn't enforce relational constraints; SQLite does |
| `ToJson()` owned entities only on SQL Server | `ToJson()` supported on SQLite | EF Core 8.0 (Nov 2023) | SQLite gained JSON column parity; however, value converter approach is still better for complex domain models |
| Newtonsoft.Json for SK serialization | System.Text.Json throughout | SemanticKernel ~1.0 | SK standardized on STJ; polymorphic support via `[JsonDerivedType]` |
| `HasData()` for seed data with JSON columns | Alternative seeding approaches | EF Core 9.0 | `HasData` incompatible with JSON columns in EF Core 9; use manual seeding if needed |
| `ChatHistory` manual serialization | Built-in STJ support | SemanticKernel ~1.x | `ChatHistory` and `ChatMessageContent` are serializable via System.Text.Json with proper options |

**Deprecated/outdated:**
- **EF Core InMemory Provider for testing:** Microsoft now explicitly recommends against it; use SQLite in-memory instead.
- **`EnsureCreated()` for production:** Only appropriate for testing/prototyping. Use `Database.Migrate()` for production to support schema evolution.

## Open Questions

1. **Domain model serialization annotations**
   - What we know: Character, Campaign, Encounter all have private constructors and private setters. System.Text.Json can handle this with `[JsonConstructor]` and `[JsonInclude]` attributes.
   - What's unclear: How many domain types need annotation changes? The `Weapon.Create()` factory pattern means Weapon has a private constructor. Same for `PowerPool.Create()`. Every type in the aggregate graph needs to be serializable.
   - Recommendation: Systematically audit each type in the aggregate graph during planning. Create a task per aggregate to add serialization support. Start with Character (most complex) to establish the pattern.

2. **Service lifetime changes (Singleton to Scoped)**
   - What we know: Current repositories are Singleton (ConcurrentDictionary). EF Core DbContext should be Scoped. Repositories that depend on DbContext must also be Scoped.
   - What's unclear: The `CharacterService`, `CampaignService`, `EncounterService` depend on repositories. If repositories become Scoped, these services must also be Scoped, or use `IServiceScopeFactory`. The console apps (top-level statements) don't use a traditional host; they build `IServiceProvider` directly.
   - Recommendation: Change domain services to Scoped. In console apps, create a scope for each "game loop iteration" or for the app's lifetime. The SK `Kernel` already manages DI; verify that scoped services work correctly with `ImportPluginFromType<T>()` which resolves from DI.

3. **ChatMessageContent serialization fidelity for tool calls**
   - What we know: SK's `ChatMessageContent.Items` can contain `FunctionCallContent` and `FunctionResultContent`. These are critical for maintaining conversation context. SK registers `[JsonDerivedType]` attributes for polymorphic serialization.
   - What's unclear: Whether all SK content types round-trip perfectly through serialization. There are known issues (GitHub #10708, #7478) with serialization of certain content types.
   - Recommendation: Write explicit round-trip tests for messages containing function calls and results. If SK's built-in serialization has gaps, store `ItemsJson` as a raw JSON string captured during the save operation rather than re-serializing reconstructed objects.

4. **Console app configuration pattern**
   - What we know: Current `Settings` class uses `ConfigurationBuilder` with env vars and user secrets. No `appsettings.json` file exists yet.
   - What's unclear: Whether to add `appsettings.json` to both console projects or extend the existing `Settings` class pattern.
   - Recommendation: Add `appsettings.json` with a `Database` section to both console projects. Extend `Settings` to include `DatabaseSettings`. The existing pattern of `GetSettings<T>()` already supports this -- just add `AddJsonFile("appsettings.json", optional: true)` to the configuration builder.

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions) - Value converter pattern for JSON serialization
- [Microsoft Learn: EF Core Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) - `Database.Migrate()` startup pattern
- [Microsoft Learn: EF Core Testing Without Database](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database) - SQLite in-memory testing
- [Microsoft Learn: System.Text.Json Polymorphism](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism) - `[JsonDerivedType]` attribute
- [Microsoft Learn: System.Text.Json Immutability](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/immutability) - `[JsonConstructor]` for private constructors
- [Microsoft Learn: Chat History in Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/chat-history) - ChatHistory object structure and management
- [GitHub: EF Core SQLite JSON Support Issue #28816](https://github.com/dotnet/efcore/issues/28816) - Confirmed shipped in EF Core 8.0.0
- [GitHub: SK ChatHistory Serialization Sample](https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/Concepts/ChatCompletion/ChatHistorySerialization.cs) - Official serialization patterns

### Secondary (MEDIUM confidence)
- [GitHub: SK Discussion #5815](https://github.com/microsoft/semantic-kernel/discussions/5815) - Best practice for persisting ChatHistory (Microsoft team recommendation: serialize + store)
- [NuGet: Microsoft.EntityFrameworkCore.Sqlite](https://www.nuget.org/packages/microsoft.entityframeworkcore.sqlite) - Latest version 10.0.3 (for .NET 10); use 9.0.x matching project's net9.0 TFM
- [GitHub: SK Discussion #12396](https://github.com/microsoft/semantic-kernel/discussions/12396) - ChatHistory serialization with orchestrations

### Tertiary (LOW confidence)
- [Medium: EF Core 9 Deep JSON Integration](https://gunesramazan.medium.com/deep-json-integration-new-capabilities-in-ef-core-9-a7e288983987) - EF Core 9 JSON capabilities overview (community source, cross-verified with official docs)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - EF Core SQLite is well-documented, stable, and the project already uses .NET 9. Package versions verified on NuGet.
- Architecture: HIGH - The JSON blob + value converter pattern is a well-established approach for DDD aggregate persistence. The relational chat history pattern follows SK team's own recommendation.
- Pitfalls: HIGH - Identified from direct code analysis of the domain model (abstract ArmorTier, private constructors, singleton instances) combined with official documentation on System.Text.Json limitations.
- Serialization specifics: MEDIUM - The exact `[JsonConstructor]`/`[JsonInclude]` annotations needed per domain type requires hands-on validation. The ArmorTier singleton converter needs testing.
- SK ChatHistory serialization: MEDIUM - Known issues exist in SK GitHub. The recommendation to store Items as serialized JSON strings is a mitigation strategy, but full-fidelity round-trip needs test validation.

**Research date:** 2026-03-02
**Valid until:** 2026-04-01 (stable domain; EF Core 9.x and SK 1.65.x are current)
