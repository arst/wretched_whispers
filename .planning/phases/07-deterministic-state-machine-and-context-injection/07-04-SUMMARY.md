---
phase: 07-deterministic-state-machine-and-context-injection
plan: 04
subsystem: api
tags: [semantic-kernel, combat-agent, chat-completion-agent, state-machine]

requires:
  - phase: 07-03
    provides: GameSessionService with stage routing, PromptComposer, wrapper plugins
provides:
  - CombatAgentService for self-contained encounter resolution
  - Combat stage routing in GameSessionService
  - CombatPrompts for combat narrator instructions
affects: [gameplay-loop, session-lifecycle]

tech-stack:
  added: []
  patterns: [sub-agent-delegation, combat-isolation]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs
    - WrtechedWhispers/WretchedWhispers.Api/Prompts/CombatPrompts.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs

key-decisions:
  - "CombatAgentService is instantiated per-combat, not registered in DI"
  - "Combat sub-agent shares kernel and DI scope with game master for transaction consistency"

patterns-established:
  - "Sub-agent delegation: GameSessionService routes to CombatAgentService when stage is Combat"

requirements-completed: [MORK-01]

duration: 15min
completed: 2026-03-25
---

# Plan 07-04: Combat Sub-Agent Summary

**CombatAgentService resolves encounters via constrained ChatCompletionAgent with only combat tools, integrated into GameSessionService stage routing**

## Performance

- **Duration:** 15 min (including human verification and bug fix)
- **Tasks:** 2 (1 implementation + 1 human verification checkpoint)
- **Files modified:** 3

## Accomplishments
- CombatAgentService with restricted tool set (EncounterWrapperPlugin, DiceWrapperPlugin only)
- Combat stage routing in GameSessionService delegates to sub-agent
- Human verification identified two stage machine bugs (character not linked to campaign, CreateCampaign creating disconnected entity)
- Both bugs fixed: CharacterWrapperPlugin auto-joins campaign, CampaignWrapperPlugin uses ConfigureCampaign

## Task Commits

1. **Task 1: CombatAgentService and GameSessionService routing** - `0027097` (feat)
2. **Task 2: Human verification** - `7e7e06c` (fix: stage machine bugs found during testing)

## Files Created/Modified
- `WrtechedWhispers/WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs` - Self-contained combat resolution agent
- `WrtechedWhispers/WretchedWhispers.Api/Prompts/CombatPrompts.cs` - Combat narrator system prompt
- `WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs` - Combat stage routing

## Decisions Made
- Combat sub-agent instantiated per-combat (not DI) for isolation
- Human verification uncovered critical stage transition bugs requiring immediate fix

## Deviations from Plan

### Auto-fixed Issues

**1. [Critical] Stage machine not advancing past CharacterCreation**
- **Found during:** Task 2 (human verification)
- **Issue:** CreateCharacter created standalone character not linked to campaign; BuildSessionContextAsync couldn't find it on next turn
- **Fix:** CharacterWrapperPlugin.CreateCharacter now auto-joins character to campaign
- **Files modified:** CharacterWrapperPlugin.cs, Campaign.cs, GameSessionService.cs
- **Verification:** 315 tests pass, stage advances correctly

**2. [Critical] CreateCampaign creating disconnected campaign entity**
- **Found during:** Task 2 (human verification)
- **Issue:** CampaignPlugin.CreateCampaign created new campaign instead of configuring existing one
- **Fix:** Replaced CreateCampaign with ConfigureCampaign; updated StagePluginRegistry and StagePrompts
- **Files modified:** CampaignWrapperPlugin.cs, StagePluginRegistry.cs, StagePrompts.cs

---

**Total deviations:** 2 critical bugs found and fixed during human verification
**Impact on plan:** Essential correctness fixes. Stage machine now functions as designed.

## Issues Encountered
- Human verification successfully caught two integration bugs that unit tests could not detect (cross-turn state persistence)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Full stage machine operational: CharacterCreation → CampaignSetup → Exploration → Combat → Resolution loop
- Combat sub-agent delegates to restricted tool set
- Ready for end-to-end playtesting

---
*Phase: 07-deterministic-state-machine-and-context-injection*
*Completed: 2026-03-25*
