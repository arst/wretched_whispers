---
phase: 01-persistence-foundation
plan: 02
subsystem: database
tags: [ef-core, sqlite, chat-history, semantic-kernel, migrations, di-wiring, repository-pattern]

# Dependency graph
requires:
  - phase: 01-persistence-foundation
    provides: WretchedWhispersDbContext, SqliteCharactersRepository, SqliteCampaignsRepository, SqliteEncountersRepository, AggregateJsonOptions, SqliteTestBase
provides:
  - IChatHistoryRepository interface in Semantic project with LoadSession, SaveMessage, CreateSession, GetSessionsForCampaign
  - SqliteChatHistoryRepository with FunctionCallContent serialization support
  - ChatSessionEntity and ChatMessageEntity with EF Core configurations
  - AddSqliteInfrastructure DI extension replacing AddInMemoryInfrastructure
  - DatabaseSettings in Settings class with appsettings.json support
  - EF Core InitialCreate migration for all 5 tables
  - DesignTimeDbContextFactory for EF Core tooling
  - Both console apps wired to SQLite with auto-migration on startup
affects: [phase-2-auth, phase-3-api]

# Tech tracking
tech-stack:
  added: [Microsoft.Extensions.Configuration.Json 8.0.x, dotnet-ef 10.0.3]
  patterns: [chat message row-per-message persistence, Transient service lifetime for SK plugin compatibility, DesignTimeDbContextFactory]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Semantic/IChatHistoryRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/ChatSessionEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/ChatMessageEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/ChatSessionConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/ChatMessageConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteChatHistoryRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/DesignTimeDbContextFactory.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Migrations/20260302143735_InitialCreate.cs
    - WrtechedWhispers/WretchedWhispers.SingleAgent.Console/appsettings.json
    - WrtechedWhispers/WretchedWhispers.Orchestration.Console/appsettings.json
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/ChatHistoryRoundTripTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/WretchedWhispersDbContext.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/ServiceCollectionExtensions.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Settings.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/WretchedWhispers.Infrastructure.csproj
    - WrtechedWhispers/WretchedWhispers.SingleAgent.Console/Program.cs
    - WrtechedWhispers/WretchedWhispers.SingleAgent.Console/WretchedWhispers.SingleAgent.Console.csproj
    - WrtechedWhispers/WretchedWhispers.Orchestration.Console/Program.cs
    - WrtechedWhispers/WretchedWhispers.Orchestration.Console/WretchedWhispers.Orchestration.Console.csproj
    - WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj
  deleted:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/CharactersInMemoryRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/CampaignsInMemoryRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/EncountersInMemoryRepository.cs

key-decisions:
  - "Transient lifetime for repositories and domain services to avoid scope issues with SK's ImportPluginFromType root provider resolution"
  - "ChatMessageContent is in Microsoft.SemanticKernel namespace, not Microsoft.SemanticKernel.ChatCompletion"
  - "DesignTimeDbContextFactory added for EF Core migration tooling independent of app startup"
  - "Orchestration.Console applies migrations only on first kernel build to avoid redundant calls across 3 agent kernels"

patterns-established:
  - "Chat message persistence: row-per-message with ItemsJson for FunctionCallContent serialization"
  - "AddSqliteInfrastructure(connectionString) as single DI entry point for all persistence"
  - "Settings.Database.ConnectionString configurable via appsettings.json with env var override"
  - "Database.Migrate() at app startup for zero-touch deployment"

requirements-completed: [INFR-02]

# Metrics
duration: 12min
completed: 2026-03-02
---

# Phase 1 Plan 02: Chat History Persistence and SQLite Switchover Summary

**IChatHistoryRepository with row-per-message SQLite persistence, AddSqliteInfrastructure DI wiring replacing all in-memory repositories, EF Core InitialCreate migration, and configurable database path via appsettings.json**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-02T14:26:26Z
- **Completed:** 2026-03-02T14:39:01Z
- **Tasks:** 2
- **Files modified:** 26 (11 created, 12 modified, 3 deleted)

## Accomplishments
- Chat history persistence via IChatHistoryRepository with full SK ChatMessageContent fidelity including FunctionCallContent round-trip
- Complete removal of all in-memory repositories and replacement with SQLite-backed persistence
- EF Core InitialCreate migration covering all 5 tables (Characters, Campaigns, Encounters, ChatSessions, ChatMessages) with proper indexes and foreign keys
- Both console apps configured with appsettings.json, AddSqliteInfrastructure, and auto-migration on startup
- 6 new round-trip tests for chat history (175 total tests, all passing)

## Task Commits

Each task was committed atomically:

1. **Task 1: IChatHistoryRepository interface, chat entities, configuration, and SQLite chat repository** - `b4b1792` (feat)
2. **Task 2: DI wiring, Settings, appsettings.json, migrations, console app switchover, and in-memory repository removal** - `6418a58` (feat)

## Files Created/Modified

**Semantic (created):**
- `IChatHistoryRepository.cs` - Interface with LoadSession, SaveMessage, CreateSession, GetSessionsForCampaign

**Infrastructure (created):**
- `Persistence/Entities/ChatSessionEntity.cs` - Session entity: Id, CampaignId, StartedAt, Messages navigation
- `Persistence/Entities/ChatMessageEntity.cs` - Message entity: Id, SessionId, Role, Content, AuthorName, ItemsJson, MetadataJson, Timestamp, OrderIndex
- `Persistence/Configurations/ChatSessionConfiguration.cs` - EF config with CampaignId index and cascade delete
- `Persistence/Configurations/ChatMessageConfiguration.cs` - EF config with SessionId index and FK
- `Persistence/Repositories/SqliteChatHistoryRepository.cs` - Full implementation mapping between SK ChatMessageContent and entity rows
- `Persistence/DesignTimeDbContextFactory.cs` - Factory for dotnet ef tooling
- `Persistence/Migrations/20260302143735_InitialCreate.cs` - Initial migration with all 5 tables

**Infrastructure (modified):**
- `ServiceCollectionExtensions.cs` - AddSqliteInfrastructure replacing AddInMemoryInfrastructure
- `Settings.cs` - Added DatabaseSettings with ConnectionString, added AddJsonFile to configuration builder
- `WretchedWhispersDbContext.cs` - Added ChatSessions and ChatMessages DbSets
- `WretchedWhispers.Infrastructure.csproj` - Added Semantic project reference and Configuration.Json package

**Console apps (modified):**
- `SingleAgent.Console/Program.cs` - Uses AddSqliteInfrastructure and Database.Migrate()
- `SingleAgent.Console/appsettings.json` - DatabaseSettings with default connection string
- `SingleAgent.Console/WretchedWhispers.SingleAgent.Console.csproj` - CopyToOutputDirectory for appsettings.json
- `Orchestration.Console/Program.cs` - Uses AddSqliteInfrastructure and Database.Migrate()
- `Orchestration.Console/appsettings.json` - DatabaseSettings with default connection string
- `Orchestration.Console/WretchedWhispers.Orchestration.Console.csproj` - CopyToOutputDirectory for appsettings.json

**Deleted:**
- `CharactersInMemoryRepository.cs`, `CampaignsInMemoryRepository.cs`, `EncountersInMemoryRepository.cs`

**Tests (created):**
- `Persistence/ChatHistoryRoundTripTests.cs` - 6 tests: session creation, text messages, function calls, ordering, null session, session isolation

## Decisions Made
- **Transient service lifetime:** SK's `ImportPluginFromType<T>` resolves plugins from the root `IServiceProvider` (not a scope). Scoped services would throw `InvalidOperationException`. Using Transient for repositories and domain services avoids this. When the web API is added in Phase 3, consider switching to Scoped with proper scope management.
- **ChatMessageContent namespace:** `ChatMessageContent` lives in `Microsoft.SemanticKernel` namespace, not `Microsoft.SemanticKernel.ChatCompletion`. Required both using directives in IChatHistoryRepository.
- **DesignTimeDbContextFactory:** Added to decouple EF Core migration tooling from the full application startup (which requires Azure OpenAI secrets). Uses a default SQLite connection string.
- **Orchestration.Console migration optimization:** The orchestration console builds 3 separate kernels (one per agent). Migrations are applied only on the first kernel build to avoid redundant calls.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ChatMessageContent namespace resolution**
- **Found during:** Task 1 (IChatHistoryRepository interface creation)
- **Issue:** `ChatMessageContent` was not found with `using Microsoft.SemanticKernel.ChatCompletion;` alone
- **Fix:** Added `using Microsoft.SemanticKernel;` alongside `using Microsoft.SemanticKernel.ChatCompletion;` since ChatMessageContent is in the root SK namespace
- **Files modified:** WretchedWhispers.Semantic/IChatHistoryRepository.cs
- **Verification:** Build succeeds
- **Committed in:** b4b1792

**2. [Rule 3 - Blocking] dotnet-ef tool not installed**
- **Found during:** Task 2 (migration creation)
- **Issue:** `dotnet ef` command not available, plus DOTNET_ROOT environment variable not set
- **Fix:** Installed dotnet-ef global tool and set DOTNET_ROOT
- **Verification:** Migration created successfully
- **Committed in:** 6418a58

**3. [Rule 3 - Blocking] EF Core Design package not found from console startup project**
- **Found during:** Task 2 (migration creation)
- **Issue:** `dotnet ef migrations add` failed when using SingleAgent.Console as startup project because it doesn't reference Design package
- **Fix:** Added DesignTimeDbContextFactory and used Infrastructure project as startup project for migration tooling
- **Verification:** Migration generated correctly with all 5 tables
- **Committed in:** 6418a58

---

**Total deviations:** 3 auto-fixed (3 blocking)
**Impact on plan:** All fixes were necessary for correct tooling and compilation. No scope creep.

## Issues Encountered
- Service lifetime choice required analysis: the plan discussed multiple approaches (Scoped, Transient, scope wrappers). Selected Transient as the pragmatic choice for console apps to avoid InvalidOperationException from SK's root provider resolution.

## User Setup Required

None - no external service configuration required. Database file is auto-created on first run.

## Next Phase Readiness
- Complete persistence foundation ready: all 3 domain aggregates + chat history stored in SQLite
- Database configurable via appsettings.json (overridable by env var for deployment)
- Migrations auto-apply on startup for zero-touch deployment
- Phase 1 complete -- ready for Phase 2 (Session Management and Resume)

## Self-Check: PASSED

All key files verified present. Both task commits (b4b1792, 6418a58) verified in git log. All 175 tests pass (6 new chat history + 169 existing).

---
*Phase: 01-persistence-foundation*
*Completed: 2026-03-02*
