---
phase: 06-mechanical-visibility-and-session-lifecycle
plan: 02
subsystem: ui
tags: [react, zustand, tailwind, dice-rolls, misery-tracker, injury-badges, status-effects]

requires:
  - phase: 06-01
    provides: "Backend SSE enrichment with injury flags, equipment condition, structured DiceRollResult"
  - phase: 04-frontend-foundation-and-character-creation
    provides: "CharacterDrawer, ToolResultCallout, EquipmentSlot, Zustand sessionStore, doom design system"
provides:
  - "MiseryTracker 7-pip doom clock component"
  - "InjuryBadges icon badge component for 6 injury types"
  - "StatusIndicators component for infection, arcane daze, encumbrance"
  - "Enriched ToolResultCallout with structured dice formula and result"
  - "EquipmentSlot with armor tier indicators and broken shield support"
  - "Extended CharacterData type with injury, status, equipment fields"
  - "Zustand store miseryCount and worldEnded state"
affects: [06-03-session-lifecycle]

tech-stack:
  added: []
  patterns:
    - "Conditional drawer sections: only render injury/status sections when any flag is active"
    - "Type guard pattern for structured SSE data (isDiceRollData)"
    - "Unicode glyph badges for game state indicators"

key-files:
  created:
    - wretched-whispers-web/src/components/character/MiseryTracker.tsx
    - wretched-whispers-web/src/components/character/InjuryBadges.tsx
    - wretched-whispers-web/src/components/character/StatusIndicators.tsx
  modified:
    - wretched-whispers-web/src/types/api.ts
    - wretched-whispers-web/src/stores/sessionStore.ts
    - wretched-whispers-web/src/app/sessions/[id]/page.tsx
    - wretched-whispers-web/src/components/chat/ToolResultCallout.tsx
    - wretched-whispers-web/src/components/character/EquipmentSlot.tsx
    - wretched-whispers-web/src/components/character/CharacterDrawer.tsx
    - wretched-whispers-web/src/components/character/CharacterDrawerToggle.tsx

key-decisions:
  - "CharacterDrawerToggle visible for ended sessions to allow reviewing character state"
  - "Armor tier cast to union type at CharacterDrawer call site for type safety"

patterns-established:
  - "Type guard for structured SSE payloads with fallback to raw rendering"
  - "Conditional section rendering in CharacterDrawer based on active flags"
  - "useRef-based previous value tracking for animation triggers in MiseryTracker"

requirements-completed: [MORK-02, MORK-03, CHAR-03, CHAR-04]

duration: 5min
completed: 2026-03-24
---

# Phase 06 Plan 02: Mechanical Visibility Components Summary

**MiseryTracker doom clock, injury/status badges, enriched dice callouts with formula breakdown, and equipment tier/shield condition in character drawer**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-24T12:58:24Z
- **Completed:** 2026-03-24T13:03:36Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments
- Extended TypeScript types, Zustand store, and page hydration with all injury, equipment, misery, and world state fields from Plan 01 backend enrichment
- Created MiseryTracker with 7 doom pips, pulse animation on new miseries, and ARIA accessibility
- Created InjuryBadges with unicode glyph badges for 6 injury types, hidden when none active
- Created StatusIndicators for infected/arcane daze/encumbered with doom color coding
- Enriched ToolResultCallout to render structured dice data (formula + result) with fallback for legacy plain values
- Extended EquipmentSlot with armor tier squares and broken shield strike-through
- Wired all components into CharacterDrawer (section order: HP, Injuries, Status, Abilities, Equipment with shield, Inventory)
- Added MiseryTracker to header via CharacterDrawerToggle, visible for ended sessions

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend TypeScript types and Zustand store** - `83d02ef` (feat)
2. **Task 2: Create mechanical visibility components and wire into drawer/header** - `80cdedc` (feat)

## Files Created/Modified
- `wretched-whispers-web/src/types/api.ts` - Extended StateUpdateEvent, CharacterData, SessionDetailDto with injury/equipment/status/world fields
- `wretched-whispers-web/src/stores/sessionStore.ts` - Added miseryCount, worldEnded to state; extended setStateUpdate mapping
- `wretched-whispers-web/src/app/sessions/[id]/page.tsx` - Updated hydrateCharacter to pass all new DTO fields
- `wretched-whispers-web/src/components/character/MiseryTracker.tsx` - 7-pip doom clock with pulse animation
- `wretched-whispers-web/src/components/character/InjuryBadges.tsx` - Icon badges for 6 injury types
- `wretched-whispers-web/src/components/character/StatusIndicators.tsx` - Status effects display
- `wretched-whispers-web/src/components/chat/ToolResultCallout.tsx` - Structured dice roll rendering with type guard
- `wretched-whispers-web/src/components/character/EquipmentSlot.tsx` - Tier indicators and broken state
- `wretched-whispers-web/src/components/character/CharacterDrawer.tsx` - Injury/status sections, shield slot, tier prop
- `wretched-whispers-web/src/components/character/CharacterDrawerToggle.tsx` - MiseryTracker in header, ended visibility

## Decisions Made
- CharacterDrawerToggle shows for "ended" sessions (in addition to "in-progress") so players can review their final character state
- Armor tier string cast to union type at the CharacterDrawer call site rather than changing the CharacterData type (backend may send arbitrary strings)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- node_modules not installed in worktree; ran npm install before tsc verification (expected for fresh worktree)

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All mechanical visibility components complete and building successfully
- Plan 06-03 (session lifecycle: end states, death/doom cards, restart flow) can proceed
- CharacterData type now carries all injury/equipment/status data from backend
- worldEnded and isDead flags in store ready for end-of-game UI in Plan 03

---
*Phase: 06-mechanical-visibility-and-session-lifecycle*
*Completed: 2026-03-24*
