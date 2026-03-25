---
phase: 06-mechanical-visibility-and-session-lifecycle
plan: 03
subsystem: ui
tags: [react, next.js, zustand, session-lifecycle, endcard, mork-borg]

requires:
  - phase: 06-01
    provides: Backend enrichment with death/worldEnded/currentDay fields in SSE and DTOs
  - phase: 06-02
    provides: MiseryTracker, InjuryBadges, StatusIndicators, enriched sessionStore with miseryCount/worldEnded
provides:
  - EndCard overlay component with death and apocalypse variants
  - ChatInput read-only mode for ended sessions
  - SessionCard skull indicator for ended sessions
  - GameSessionPage lifecycle integration (end card after streaming completes)
  - Session restart flow via POST /sessions and router navigation
affects: []

tech-stack:
  added: []
  patterns:
    - EndCard fade-in animation via useState + CSS transition
    - Conditional overlay render gated on status === ended AND !isStreaming

key-files:
  created:
    - wretched-whispers-web/src/components/session/EndCard.tsx
  modified:
    - wretched-whispers-web/src/components/chat/ChatInput.tsx
    - wretched-whispers-web/src/components/session/SessionCard.tsx
    - wretched-whispers-web/src/app/sessions/[id]/page.tsx
    - wretched-whispers-web/src/stores/sessionStore.ts

key-decisions:
  - "End card appears only after streaming completes to avoid interrupting narrator farewell"
  - "Apocalypse variant takes priority over death (worldEnded checked first)"
  - "EndCard handles restart internally via apiFetch + router.push"

patterns-established:
  - "Overlay gating: status === ended && !isStreaming prevents premature UI interruption"
  - "Session restart: POST /sessions + router.push for frictionless new game"

requirements-completed: [MORK-01]

duration: 12min
completed: 2026-03-24
---

# Phase 06 Plan 03: Session Lifecycle Summary

**EndCard overlay with death/apocalypse variants, read-only ended sessions, skull indicators, and Begin Anew restart flow**

## Performance

- **Duration:** 12 min (across previous executor + checkpoint approval)
- **Started:** 2026-03-24T11:35:00Z
- **Completed:** 2026-03-24T14:45:33Z
- **Tasks:** 3 (2 auto + 1 human-verify checkpoint)
- **Files modified:** 5

## Accomplishments
- EndCard overlay component with two atmospheric variants: pink "YOUR WRETCH HAS FALLEN" for death and yellow "THE WORLD HAS ENDED" for apocalypse
- "Begin Anew" restart creates new session via POST /sessions and navigates seamlessly
- ChatInput shows "This tale has ended..." with disabled state and hidden SEND button for ended sessions
- SessionCard displays skull glyph and reduced opacity for ended sessions in the list
- End card timing gated on streaming completion so narrator farewell plays fully before overlay

## Task Commits

Each task was committed atomically:

1. **Task 1: Create EndCard component and update ChatInput for read-only ended sessions** - `b0ba4ab` (feat)
2. **Task 2: Integrate EndCard into GameSessionPage with lifecycle transitions** - `b803f95` (feat)
3. **Task 3: Visual verification of complete session lifecycle** - checkpoint approved by user

## Files Created/Modified
- `wretched-whispers-web/src/components/session/EndCard.tsx` - End-of-game overlay with death/apocalypse variants, fade-in animation, restart handler
- `wretched-whispers-web/src/components/chat/ChatInput.tsx` - Read-only mode for ended sessions with "This tale has ended..." placeholder
- `wretched-whispers-web/src/components/session/SessionCard.tsx` - Skull glyph indicator and opacity reduction for ended sessions
- `wretched-whispers-web/src/app/sessions/[id]/page.tsx` - EndCard integration, conditional render after streaming, store selectors for lifecycle fields
- `wretched-whispers-web/src/stores/sessionStore.ts` - Added currentDay field alongside Plan 02's miseryCount/worldEnded

## Decisions Made
- End card appears only after streaming completes (`status === "ended" && !isStreaming`) to avoid interrupting the narrator's farewell message
- Apocalypse variant (yellow) takes visual priority over death (pink) when worldEnded is true, matching Mork Borg lore where the 7th Misery destroys everything
- EndCard handles restart internally via `apiFetch("/sessions", { method: "POST" })` and `router.push` rather than delegating to parent

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 06 is now complete: all 3 plans (backend enrichment, frontend mechanical visibility, session lifecycle) delivered
- The complete Mork Borg gameplay loop is closed: character creation -> gameplay with visible mechanics -> death/apocalypse end state -> frictionless restart
- Ready for any future phases (polish, additional features, deployment)

## Self-Check: PASSED

All 5 created/modified files verified on disk. Both task commits (b0ba4ab, b803f95) verified in git history.

---
*Phase: 06-mechanical-visibility-and-session-lifecycle*
*Completed: 2026-03-24*
