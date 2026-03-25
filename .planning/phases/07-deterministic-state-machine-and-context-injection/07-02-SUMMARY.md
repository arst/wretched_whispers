---
phase: "07"
plan: "02"
subsystem: "semantic-kernel-plugins"
tags: [wrapper-plugins, id-auto-fill, guardrails, stage-filtering, tdd]
dependency_graph:
  requires: []
  provides: [wrapper-plugins, stage-plugin-registry, session-context-stub]
  affects: [game-session-service, kernel-builder]
tech_stack:
  added: []
  patterns: [interface-based-delegation, per-stage-function-gating, corrective-guardrails]
key_files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/CharacterWrapperPlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/CampaignWrapperPlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/EncounterWrapperPlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/DiceWrapperPlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/ResolutionWrapperPlugin.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/ICharacterOperations.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/ICampaignOperations.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/IEncounterOperations.cs
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/IDiceOperations.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/StagePluginRegistry.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/SessionContext.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/SessionStage.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs
    - WrtechedWhispers/WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs
  modified: []
decisions:
  - "Interface-based delegation (ICharacterOperations etc.) instead of direct plugin class dependency for testability since original plugin methods are not virtual"
  - "SessionContext and SessionStage created as stubs for cross-plan contract with Plan 01 (parallel wave)"
  - "ResolutionWrapperPlugin signals completion by clearing ActiveEncounterId rather than calling Encounter.Resolve (which does not exist on the domain)"
metrics:
  duration: "11min"
  completed: "2026-03-25T06:27:22Z"
  tasks_completed: 2
  tasks_total: 2
  tests_added: 23
  tests_total: 275
---

# Phase 07 Plan 02: Wrapper Plugins and Stage Function Filtering Summary

Wrapper plugins hide all GUIDs from the model via interface-based delegation to original plugins, with SessionContext auto-filling IDs and corrective guardrails steering the model back on invalid calls. StagePluginRegistry maps 6 stages to exact function lists for FunctionChoiceBehavior filtering.

## Task Results

| Task | Name | Commit | Tests |
|------|------|--------|-------|
| 1 | Wrapper plugins with ID auto-fill and guardrails | fded7a5 | 16 |
| 2 | StagePluginRegistry with per-stage function filtering | ffd15eb | 7 |

## What Was Built

### Task 1: Wrapper Plugins

Five wrapper plugins that sit between the Semantic Kernel agent and the original domain plugins:

- **CharacterWrapperPlugin** -- auto-fills characterId on all methods (except scrollId/itemId which model selects). Guardrails prevent duplicate character creation with corrective "already exists" message.
- **CampaignWrapperPlugin** -- auto-fills campaignId. AddCharacterToCampaign and StartCampaign take ZERO parameters. Corrective messages guide model to call CreateCampaign/CreateCharacter first. EndCampaign deliberately not exposed (D-13).
- **EncounterWrapperPlugin** -- auto-fills encounterId and playerBeingAttackedId/attackingPlayerId. AttackPlayer takes only adversaryId parameter.
- **DiceWrapperPlugin** -- thin delegation wrapper for consistent naming.
- **ResolutionWrapperPlugin** -- NEW plugin with CompleteResolution() that clears ActiveEncounterId from context, signaling return to exploration.

Four operation interfaces (ICharacterOperations, ICampaignOperations, IEncounterOperations, IDiceOperations) enable Moq-based testing without requiring virtual methods on original plugins.

### Task 2: StagePluginRegistry

Maps each SessionStage to the exact list of KernelFunction objects the model may call:
- CharacterCreation: 1 function
- CampaignSetup: 3 functions
- Exploration: 10 functions
- Combat: 4 functions
- Resolution: 8 functions
- Ended: 0 functions (empty)

Uses Kernel.Plugins.GetFunction() to resolve real KernelFunction instances by plugin name and function name.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Original plugin methods not virtual -- cannot mock with Moq**
- **Found during:** Task 1
- **Issue:** Plan specified mocking inner plugins directly with Moq, but CharacterPlugin/CampaignPlugin/EncounterPlugin methods are not virtual.
- **Fix:** Extracted operation interfaces (ICharacterOperations, ICampaignOperations, IEncounterOperations, IDiceOperations) that wrapper plugins depend on via constructor injection. Original plugins will implement these interfaces when wired in DI (adapter pattern). Tests use Moq on the interfaces.
- **Files created:** ICharacterOperations.cs, ICampaignOperations.cs, IEncounterOperations.cs, IDiceOperations.cs
- **Commit:** fded7a5

**2. [Rule 3 - Blocking] SessionContext and SessionStage not yet created by Plan 01**
- **Found during:** Task 1
- **Issue:** Plan 01 (parallel wave 1) creates SessionContext, but may not have completed. Plan noted: "create a minimal SessionContext stub if Plan 01 hasn't completed yet."
- **Fix:** Created SessionContext.cs and SessionStage.cs as stubs matching the agreed contract from the plan interfaces section.
- **Files created:** SessionContext.cs, SessionStage.cs
- **Commit:** fded7a5

## Known Stubs

None -- all wrapper plugins are fully implemented with real delegation logic and guardrails. The SessionContext/SessionStage stubs are minimal but complete per the cross-plan contract.

## Verification

- `dotnet test --filter "WrapperPlugin"`: 16 passed
- `dotnet test --filter "StagePluginRegistry"`: 7 passed
- `dotnet test` (full suite): 275 passed, 0 failed

## Self-Check: PASSED
