---
phase: 01-persistence-foundation
plan: 01
subsystem: database
tags: [ef-core, sqlite, system-text-json, json-blob, serialization, repository-pattern]

# Dependency graph
requires: []
provides:
  - WretchedWhispersDbContext with DbSets for Character, Campaign, Encounter entities
  - SqliteCharactersRepository, SqliteCampaignsRepository, SqliteEncountersRepository
  - AggregateJsonOptions with ArmorTierConverter for polymorphic serialization
  - JSON serialization annotations on all domain aggregate types
  - SqliteTestBase for in-memory SQLite test infrastructure
affects: [01-02-PLAN, phase-2-auth, phase-3-api]

# Tech tracking
tech-stack:
  added: [Microsoft.EntityFrameworkCore.Sqlite 9.0.x, Microsoft.EntityFrameworkCore.Design 9.0.x]
  patterns: [JSON blob persistence, thin entity wrappers, custom JsonConverter for polymorphic hierarchies, internal properties for STJ binding]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/WretchedWhispersDbContext.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/CharacterEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/CampaignEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Entities/EncounterEntity.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/CharacterEntityConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/CampaignEntityConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Configurations/EncounterEntityConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Serialization/AggregateJsonOptions.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Serialization/ArmorTierConverter.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteCharactersRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteCampaignsRepository.cs
    - WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/Repositories/SqliteEncountersRepository.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/JsonSerializationTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/SqliteTestBase.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/CharacterRoundTripTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/CampaignRoundTripTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Persistence/EncounterRoundTripTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Infrastructure/WretchedWhispers.Infrastructure.csproj
    - WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Character.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Abilities/Abilities.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Abilities/AbilityScore.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Possessions/Armors/Armor.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Possessions/Weapons/Weapon.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Possessions/Shield.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Possessions/Scrolls/Scroll.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Powers/PowerPool.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/Omens.cs
    - WrtechedWhispers/WretchedWhispers.Core/Characters/InventoryItem.cs
    - WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs
    - WrtechedWhispers/WretchedWhispers.Core/Campaigns/World/CalendarOfNechrubel.cs
    - WrtechedWhispers/WretchedWhispers.Core/Campaigns/World/Misery.cs
    - WrtechedWhispers/WretchedWhispers.Core/Encounters/Encounter.cs
    - WrtechedWhispers/WretchedWhispers.Core/Adversaries/Adversary.cs

key-decisions:
  - "Used internal properties instead of private fields for STJ constructor parameter binding compatibility"
  - "ArmorTier polymorphism handled via custom JsonConverter with $type discriminator rather than [JsonDerivedType]"
  - "AbilityScore constructor parameter renamed from value to modifier to match property name for STJ binding"
  - "Encounter.Adversaries changed from IReadOnlyList to public List for STJ round-trip support"

patterns-established:
  - "JSON blob persistence: Guid Id PK + string Data TEXT per aggregate table"
  - "Repository pattern: FindAsync -> deserialize Data / serialize -> upsert entity"
  - "SqliteTestBase: in-memory SQLite with kept-open connection for test isolation"
  - "STJ binding rule: constructor param names must match property names (case-insensitive)"

requirements-completed: [INFR-02]

# Metrics
duration: 3min
completed: 2026-03-02
---

# Phase 1 Plan 01: Persistence Foundation Summary

**EF Core SQLite persistence with JSON blob pattern for Character/Campaign/Encounter aggregates, custom ArmorTier polymorphic converter, and 17 round-trip tests**

## Performance

- **Duration:** 3 min
- **Started:** 2026-03-02T14:19:36Z
- **Completed:** 2026-03-02T14:23:03Z
- **Tasks:** 2
- **Files modified:** 34 (17 created, 17 modified)

## Accomplishments
- Full JSON serialization support for all three domain aggregate graphs (Character, Campaign, Encounter) with correct round-trip fidelity
- Three SQLite repository implementations (SqliteCharactersRepository, SqliteCampaignsRepository, SqliteEncountersRepository) using JSON blob pattern
- Custom ArmorTierConverter handling polymorphic serialization of 4 armor tier types via $type discriminator
- 17 persistence tests covering JSON round-trips, SQLite save/load, update/reload, null handling, and full equipment loadouts

## Task Commits

Each task was committed atomically:

1. **Task 1: EF Core packages, entity wrappers, DbContext, JSON serialization config, and domain model annotations** - `b3c85c9` (feat)
2. **Task 2: SQLite aggregate repositories with round-trip tests against in-memory SQLite** - `8a82094` (feat)

## Files Created/Modified

**Infrastructure (created):**
- `Persistence/Entities/CharacterEntity.cs` - Thin wrapper: Guid Id + string Data
- `Persistence/Entities/CampaignEntity.cs` - Same pattern
- `Persistence/Entities/EncounterEntity.cs` - Same pattern
- `Persistence/Configurations/*EntityConfiguration.cs` - EF Core table configs (3 files)
- `Persistence/WretchedWhispersDbContext.cs` - DbContext with 3 DbSets
- `Persistence/Serialization/AggregateJsonOptions.cs` - Shared JSON options factory
- `Persistence/Serialization/ArmorTierConverter.cs` - Custom polymorphic converter
- `Persistence/Repositories/SqliteCharactersRepository.cs` - ICharactersRepository impl
- `Persistence/Repositories/SqliteCampaignsRepository.cs` - ICampaignsRepository impl
- `Persistence/Repositories/SqliteEncountersRepository.cs` - IEncountersRepository impl

**Tests (created):**
- `Persistence/JsonSerializationTests.cs` - 10 JSON round-trip tests
- `Persistence/SqliteTestBase.cs` - In-memory SQLite test base with kept-open connection
- `Persistence/CharacterRoundTripTests.cs` - 4 character repository tests
- `Persistence/CampaignRoundTripTests.cs` - 2 campaign repository tests
- `Persistence/EncounterRoundTripTests.cs` - 2 encounter repository tests

**Domain model (modified for serialization annotations):**
- `Character.cs` - [JsonConstructor], [JsonInclude] on private-setter props, Scrolls as List
- `Abilities.cs` - [JsonConstructor] with renamed params matching properties
- `AbilityScore.cs` - [JsonConstructor] with modifier param name
- `Armor.cs` - Refactored from primary constructor, added OriginalTier
- `Weapon.cs` - [JsonConstructor] with damageDie param
- `Shield.cs` - [JsonInclude] on IsBroken
- `Scroll.cs` - [JsonConstructor]
- `PowerPool.cs` - [JsonConstructor] with usesRemaining/maxUses
- `Omens.cs` - [JsonConstructor]
- `InventoryItem.cs` - Refactored from primary constructor
- `Campaign.cs` - Converted private fields to internal properties
- `CalendarOfNechrubel.cs` - TriggeredMiseries as internal property
- `Misery.cs` - Refactored from primary constructor
- `Encounter.cs` - Adversaries as public List, converted from private field
- `Adversary.cs` - [JsonConstructor] with Id param, refactored from primary constructor

## Decisions Made
- **STJ binding via internal properties:** System.Text.Json matches constructor parameters by property/field NAME (not [JsonPropertyName]). Private fields like `_adversaries` cannot bind to constructor param `adversaries`. Solution: convert private fields to internal/public properties with names matching constructor params.
- **ArmorTier custom converter:** Used custom JsonConverter with `$type` discriminator instead of [JsonDerivedType] because the ArmorTier hierarchy uses singleton instances that need to be preserved.
- **AbilityScore param rename:** Renamed constructor param from `value` to `modifier` to match the `Modifier` property for STJ binding.
- **Encounter.Adversaries type change:** Changed from `IReadOnlyList<Adversary>` to `List<Adversary>` to enable STJ deserialization while maintaining backward compatibility (List implements IReadOnlyList).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed AbilityScore STJ constructor param binding**
- **Found during:** Task 1 (JSON serialization tests)
- **Issue:** AbilityScore constructor had param named `value` but property is `Modifier` -- STJ cannot bind them
- **Fix:** Renamed constructor parameter from `value` to `modifier`
- **Files modified:** WretchedWhispers.Core/Characters/Abilities/AbilityScore.cs
- **Verification:** Character_RoundTrips_BasicProperties test passes (Agility.Modifier preserved)
- **Committed in:** b3c85c9

**2. [Rule 1 - Bug] Fixed STJ private field binding for Campaign, Encounter, CalendarOfNechrubel**
- **Found during:** Task 1 (JSON serialization tests)
- **Issue:** STJ constructor parameter binding fails when private fields like `_adversaries`, `_calender`, `_triggeredMiseries` don't match constructor param names `adversaries`, `calender`, `triggeredMiseries`
- **Fix:** Converted all private backing fields to internal/public properties with matching names
- **Files modified:** Campaign.cs, Encounter.cs, CalendarOfNechrubel.cs
- **Verification:** All round-trip tests pass for Campaign, Encounter
- **Committed in:** b3c85c9

---

**Total deviations:** 2 auto-fixed (2 bugs)
**Impact on plan:** Both fixes were necessary for correct JSON serialization. No scope creep.

## Issues Encountered
- .NET SDK was not installed on the system -- installed .NET 9.0.311 SDK and .NET 8 runtime to `~/.dotnet`
- NuGet package version conflict between `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.7 and 9.0.13 (required by EF Core) -- resolved by using version wildcard `9.0.*`
- Pre-existing flaky test failures in PowerPoolTests and CalendarOfNechrubelTests due to shared static Dice mock state -- these are test isolation issues, not caused by this plan's changes

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All three repository implementations ready for DI wiring (Plan 01-02)
- AggregateJsonOptions ready for reuse in chat history serialization
- SqliteTestBase available for Plan 01-02 persistence tests
- DbContext ready for chat history DbSets and migrations

## Self-Check: PASSED

All 13 key files verified present. Both task commits (b3c85c9, 8a82094) verified in git log. All 169 tests pass (17 new persistence tests + 152 existing).

---
*Phase: 01-persistence-foundation*
*Completed: 2026-03-02*
