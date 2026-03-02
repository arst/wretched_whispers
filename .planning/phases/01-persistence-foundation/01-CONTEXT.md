# Phase 1: Persistence Foundation - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace all in-memory `ConcurrentDictionary` repositories with SQLite/EF Core persistence. All domain aggregates (Character, Campaign, Encounter) and SemanticKernel chat history must survive application restarts and reload with full fidelity. The existing console applications switch from in-memory to SQLite storage.

</domain>

<decisions>
## Implementation Decisions

### Value Object Mapping
- Single JSON blob per aggregate: schema is `(Id GUID PK, Data JSON)`
- All value objects, collections, and status flags serialized into one JSON column
- Consistent pattern across all three aggregates (Character, Campaign, Encounter)
- No separate tables for nested objects — Inventory items, Scrolls, Abilities, HitPoints, status flags all live in the JSON blob
- EF Core 8+ JSON column support for mapping

### Chat History Storage
- Individual message rows, not a JSON blob per session
- Each row stores: Id, SessionId, Role, Content, Timestamp, AuthorName
- Full SemanticKernel metadata persisted — including tool call info and function results
- Enables future message history display and scrollback
- New `IChatHistoryRepository` interface defined in WretchedWhispers.Semantic project (not Core — ChatHistory is an SK type)
- Session-based grouping: Session entity (SessionId, CampaignId, StartedAt) groups messages

### Database Structure
- Single SQLite file for everything (aggregates + chat history + sessions)
- File lives in working directory: `./wretched-whispers.db`
- Path configurable via `appsettings.json` Database section (overridable by env var for Coolify deployment)
- Self-contained deployment target — Coolify with volume mount at app root
- EF Core code-first migrations checked into source control
- Migrations auto-apply on application startup (zero-touch deployment)

### Console App Wiring
- Replace `AddInMemoryInfrastructure()` entirely — no parallel in-memory option
- Both SingleAgent.Console and Orchestration.Console switch to SQLite
- Tests use SQLite in-memory mode (`:memory:`) instead of the old `ConcurrentDictionary` repositories
- Database connection string configured via `appsettings.json` section, following the existing Settings pattern

### Claude's Discretion
- Exact EF Core entity configuration details
- JSON serialization settings (System.Text.Json options)
- Migration naming conventions
- DbContext internal organization
- Error handling for corrupt or missing database files

</decisions>

<specifics>
## Specific Ideas

- App should be as self-contained as possible — single DB file, no external dependencies
- Coolify deployment model: volume mount for persistence, env var overrides for config
- SQLite `:memory:` for test isolation instead of maintaining separate in-memory repository implementations

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ICharactersRepository` (Core): Simple Get/Save interface — SQLite implementation replaces `CharactersInMemoryRepository`
- `ICampaignsRepository` (Core): Simple Get/Save interface — SQLite implementation replaces `CampaignsInMemoryRepository`
- `IEncountersRepository` (Core): Simple Get/Save interface — SQLite implementation replaces `EncountersInMemoryRepository`
- `ServiceCollectionExtensions.AddInMemoryInfrastructure()` (Infrastructure): DI registration pattern to follow/replace

### Established Patterns
- Repository interfaces in Core with implementations in Infrastructure
- DI registration via extension methods on `IServiceCollection`
- Configuration via `Settings` class binding from user secrets / appsettings
- SemanticKernel plugins consume repositories via constructor injection

### Integration Points
- `Program.cs` in both console apps calls `RegisterServices(IKernelBuilder)` → `AddInMemoryInfrastructure()`
- `ChatHistorySummarizationReducer` in SingleAgent.Console needs persisted history to resume
- `ChatHistory` created in `Program.cs` — needs to be loaded from DB on resume
- SK plugins (`CharacterPlugin`, `CampaignPlugin`, `EncounterPlugin`) depend on repository interfaces — transparent swap

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-persistence-foundation*
*Context gathered: 2026-03-02*
