---
phase: 07-deterministic-state-machine-and-context-injection
plan: 03
subsystem: api
tags: [prompt-composition, stage-machine-integration, wrapper-plugins, function-filtering, di-wiring]

requires:
  - phase: 07-deterministic-state-machine-and-context-injection
    plan: 01
    provides: SessionStage, SessionContext, StageTransitionFilter
  - phase: 07-deterministic-state-machine-and-context-injection
    plan: 02
    provides: Wrapper plugins, StagePluginRegistry, operation interfaces
provides:
  - NarratorPersona static class with doom-metal tone guidance
  - StagePrompts static class with per-stage instruction fragments for all 6 stages
  - PromptComposer service composing persona + stage instructions + context snapshot
  - Rewritten GameSessionService using SessionContext, wrapper plugins, function filtering, StageTransitionFilter
  - Plugin adapter classes bridging original plugins to operation interfaces
  - DI registration for StagePluginRegistry and PromptComposer
affects: [07-04, frontend-state-update]

tech-stack:
  added: []
  patterns: [adapter-pattern, composed-system-prompt, per-turn-session-context]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Prompts/NarratorPersona.cs
    - WrtechedWhispers/WretchedWhispers.Api/Prompts/StagePrompts.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/PromptComposer.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Prompts/PromptComposerTests.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/CharacterPluginAdapter.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/CampaignPluginAdapter.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/EncounterPluginAdapter.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/DicePluginAdapter.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs
    - WrtechedWhispers/WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs

key-decisions:
  - "Plugin adapter pattern: created 4 adapter classes (CharacterPluginAdapter, CampaignPluginAdapter, EncounterPluginAdapter, DicePluginAdapter) to bridge original Semantic plugins to Api operation interfaces since they live in different projects"
  - "SessionContext built twice per turn: once before agent invocation (for prompt composition and function filtering), once after commit (for accurate state_update SSE emission)"
  - "state_update SSE event includes both 'stage' (new 6-state enum) and 'status' (legacy 3-state) for frontend backward compatibility"

patterns-established:
  - "Composed system prompt: NarratorPersona (fixed) + StagePrompts.For(stage) (dynamic) + SessionContext.FormatSnapshot() (dynamic)"
  - "Per-turn kernel build: wrapper plugins instantiated with fresh SessionContext, not registered in DI"
  - "Adapter pattern: thin delegation classes bridging cross-project interface boundaries"

requirements-completed: [MORK-01]

duration: 12min
completed: 2026-03-25
---

# Phase 07 Plan 03: Prompt Composition and GameSessionService Integration Summary

**Dynamic system prompts composed from narrator persona + per-stage instructions + context snapshot, with GameSessionService rewritten to use SessionContext, wrapper plugins, stage-based function filtering via FunctionChoiceBehavior.Auto(functions:), and StageTransitionFilter**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-25T06:36:43Z
- **Completed:** 2026-03-25T06:48:43Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments

- Extracted doom-metal narrator persona from monolithic 14-step instructions into static NarratorPersona class
- Created 6 focused stage instruction fragments in StagePrompts replacing the monolithic session flow
- PromptComposer assembles all 3 prompt fragments (persona + stage + snapshot) per turn
- GameSessionService fully rewritten to build SessionContext from campaign/character/encounter state at turn start
- Wrapper plugins (from Plan 02) imported into kernel instead of original plugins -- model never sees GUIDs
- StageTransitionFilter registered on kernel for post-execution transition validation
- FunctionChoiceBehavior.Auto(functions: allowedFunctions) restricts model to stage-appropriate tools
- state_update SSE event now includes `stage` field alongside legacy `status` for backward compatibility
- 4 adapter classes bridge original Semantic plugins to Api operation interfaces
- DI properly configured with StagePluginRegistry and PromptComposer as Scoped services

## Task Commits

| Task | Name | Commit | Tests |
|------|------|--------|-------|
| 1 | NarratorPersona, StagePrompts, PromptComposer with tests (TDD) | 005c178 | 18 |
| 2 | Rewrite GameSessionService and DI registration | 227b793 | 0 (integration via existing 318 tests) |

_Task 1 followed TDD: tests written first (RED), implementation second (GREEN)._

## Files Created/Modified

- `WretchedWhispers.Api/Prompts/NarratorPersona.cs` - Static class with doom-metal narrator persona text
- `WretchedWhispers.Api/Prompts/StagePrompts.cs` - Static class with For(SessionStage) returning per-stage instructions
- `WretchedWhispers.Api/Services/PromptComposer.cs` - Composes persona + stage instructions + snapshot
- `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs` - 18 tests for composition and stage content
- `WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/CharacterPluginAdapter.cs` - Adapts CharacterPlugin to ICharacterOperations
- `WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/CampaignPluginAdapter.cs` - Adapts CampaignPlugin to ICampaignOperations
- `WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/EncounterPluginAdapter.cs` - Adapts EncounterPlugin to IEncounterOperations
- `WretchedWhispers.Api/Plugins/GameMasterPlugins/Adapters/DicePluginAdapter.cs` - Adapts DicePlugin to IDiceOperations
- `WretchedWhispers.Api/Services/GameSessionService.cs` - Rewritten with stage machine integration
- `WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs` - Added StagePluginRegistry and PromptComposer DI

## Decisions Made

- Plugin adapter pattern chosen because original plugins (Semantic project) cannot implement interfaces defined in Api project without circular dependency. Thin adapters delegate all calls.
- SessionContext built twice per turn: pre-turn for prompt composition/function filtering, post-commit for accurate state_update emission reflecting mutations made during the turn.
- Legacy `status` field kept in state_update SSE alongside new `stage` field to avoid breaking the frontend during incremental migration.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Wrapper plugins require operation interfaces, not original plugin classes**
- **Found during:** Task 2
- **Issue:** Plan specified `serviceProvider.GetRequiredService<CharacterPlugin>()` passed directly to wrapper plugin constructors, but Plan 02 implemented wrapper plugins with `ICharacterOperations`/`ICampaignOperations`/`IEncounterOperations`/`IDiceOperations` interfaces. Original plugins don't implement these interfaces (cross-project boundary).
- **Fix:** Created 4 adapter classes in `Adapters/` directory that implement the operation interfaces by delegating to original plugins.
- **Files created:** CharacterPluginAdapter.cs, CampaignPluginAdapter.cs, EncounterPluginAdapter.cs, DicePluginAdapter.cs
- **Commit:** 227b793

---

**Total deviations:** 1 auto-fixed (blocking)
**Impact on plan:** Adapter pattern adds 4 thin files but preserves the intended architecture. No scope creep.

## Issues Encountered

None beyond the deviation documented above.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None - all planned functionality is fully implemented and wired.

## Verification

- `dotnet build` succeeds with no errors
- `dotnet test` passes all 318 tests (existing + 18 new PromptComposer tests)
- NarratorPersona.Text contains doom metal tone guidance
- StagePrompts.For() returns non-empty string for all 6 stages
- GameSessionService imports wrapper plugins (not original plugins)
- FunctionChoiceBehavior.Auto(functions: allowedFunctions) active
- StageTransitionFilter registered on kernel
- state_update SSE event includes `stage` field

## Self-Check: PASSED

All 8 created files verified present. Both task commits (005c178, 227b793) verified in git log. 318/318 tests passing.
