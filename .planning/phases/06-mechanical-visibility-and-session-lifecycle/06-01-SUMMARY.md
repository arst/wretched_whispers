---
phase: 06-mechanical-visibility-and-session-lifecycle
plan: 01
subsystem: api
tags: [sse, dto, dice, injuries, domain, semantic-kernel]

requires:
  - phase: 03-api-layer-and-streaming
    provides: SSE streaming infrastructure, GameSessionService, SessionEndpoints
  - phase: 01-persistence-foundation
    provides: Character domain model with injuries, equipment, shield
provides:
  - DiceRollResult structured return type for Semantic Kernel dice plugin
  - Enriched SSE state_update payload with 14 new fields (injuries, status, equipment, world state)
  - Enriched SessionDetailDto REST response with matching 14 fields
  - Campaign.WorldEnded public property
  - DeriveStatus unit tests covering all 3 status branches
affects: [06-02-PLAN, 06-03-PLAN, frontend-character-drawer, frontend-session-end]

tech-stack:
  added: []
  patterns:
    - "DiceRollResult record with [Description] attributes for Semantic Kernel function serialization"
    - "Parallel armorTier field (lowercase machine-readable) alongside CharacterArmor (display-friendly)"

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Tests/DicePluginTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DeriveStatusTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Semantic/DicePlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs
    - WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/SessionDetailDto.cs
    - WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs

key-decisions:
  - "Added Campaign.WorldEnded public property to expose internal Calendar.WorldEnded without InternalsVisibleTo"
  - "DeriveStatus tests verify observable Campaign state rather than private method directly"

patterns-established:
  - "DiceRollResult record: structured Semantic Kernel function return with Description metadata"
  - "Lowercase armorTier for SSE/DTO (machine-readable) vs display CharacterArmor (human-readable)"

requirements-completed: [MORK-03, CHAR-03, CHAR-04, MORK-01]

duration: 8min
completed: 2026-03-24
---

# Phase 06 Plan 01: Backend Enrichment Summary

**Structured DiceRollResult, 14-field SSE/REST payload enrichment (injuries, equipment condition, death, worldEnded), and DeriveStatus unit tests**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-24T12:46:00Z
- **Completed:** 2026-03-24T12:53:36Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- DicePlugin returns structured DiceRollResult with formula and numeric result (enables frontend dice breakdown display)
- SSE state_update and SessionDetailDto both carry all 14 new fields: 6 injury flags, 3 status conditions, isDead, armorTier, hasShield, isShieldBroken, worldEnded
- 5 DeriveStatus unit tests covering all status branches (character-creation, in-progress, ended via death, ended via world-end)
- All 252 tests pass including 3 new DicePlugin tests and 5 new DeriveStatus tests

## Task Commits

Each task was committed atomically:

1. **Task 1: DicePlugin structured return type (TDD RED)** - `f1930d2` (test)
2. **Task 1: DicePlugin structured return type (TDD GREEN)** - `8ee4730` (feat)
3. **Task 2: Enrich state_update, SessionDetailDto, DeriveStatus tests** - `a70c7f9` (feat)

_Note: TDD task had separate RED and GREEN commits_

## Files Created/Modified
- `WrtechedWhispers/WretchedWhispers.Semantic/DicePlugin.cs` - DiceRollResult record + structured Roll return
- `WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs` - 14 new fields in SSE state_update payload
- `WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` - 14 new fields in GetSessionDetail
- `WrtechedWhispers/WretchedWhispers.Api/Models/SessionDetailDto.cs` - 14 new optional parameters
- `WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs` - Public WorldEnded property
- `WrtechedWhispers/WretchedWhispers.Tests/DicePluginTests.cs` - 3 tests for DiceRollResult
- `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DeriveStatusTests.cs` - 5 DeriveStatus state tests
- `WrtechedWhispers/WretchedWhispers.*.csproj` (7 files) - TFM upgrade net9.0 to net10.0

## Decisions Made
- Added `Campaign.WorldEnded` as a public property delegating to `Calendar.WorldEnded`, keeping Calendar internal while exposing the needed data through Campaign's public API
- DeriveStatus tests verify the observable Campaign domain state (IsActive, Players.Count, WorldEnded) rather than testing the private static method directly
- Lowercase armorTier values ("none", "light", "medium", "heavy") in SSE/DTO for machine consumption, separate from existing display-friendly CharacterArmor values

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Upgraded all csproj from net9.0 to net10.0**
- **Found during:** Task 1 (DicePlugin tests)
- **Issue:** All projects target net9.0 but only net10.0 runtime is installed; tests abort with "You must install or update .NET"
- **Fix:** Changed TargetFramework from net9.0 to net10.0 in all 7 csproj files
- **Files modified:** All *.csproj files in the solution
- **Verification:** dotnet test passes with all 252 tests
- **Committed in:** 8ee4730 (Task 1 GREEN commit)

**2. [Rule 3 - Blocking] Added Campaign.WorldEnded public property**
- **Found during:** Task 2 (SessionEndpoints enrichment)
- **Issue:** Plan references `campaign.Calendar.WorldEnded` but Calendar is internal; API project cannot access it
- **Fix:** Added `[JsonIgnore] public bool WorldEnded => Calendar.WorldEnded;` to Campaign, following existing Miseries pattern
- **Files modified:** WrtechedWhispers/WretchedWhispers.Core/Campaigns/Campaign.cs
- **Verification:** Build succeeds, all tests pass
- **Committed in:** a70c7f9 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes necessary to complete tasks. TFM upgrade aligns with environment. WorldEnded property follows existing domain patterns.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All backend payloads enriched and ready for frontend consumption
- Plan 06-02 (frontend visibility components) can consume the 14 new SSE/REST fields
- Plan 06-03 (session lifecycle) can use WorldEnded and isDead for end-state UI

## Self-Check: PASSED

All 8 key files verified present. All 3 task commits verified in git log (f1930d2, 8ee4730, a70c7f9).

---
*Phase: 06-mechanical-visibility-and-session-lifecycle*
*Completed: 2026-03-24*
