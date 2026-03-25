# Phase 6: Mechanical Visibility and Session Lifecycle - Context

**Gathered:** 2026-03-24
**Status:** Ready for planning

<domain>
## Phase Boundary

Players can see the real dice rolls and mechanical outcomes behind the narrative, track the world's doom via a 7-slot Misery clock, monitor their character's injuries and equipment condition, and play through a complete Mork Borg session from creation to death or apocalypse. This phase delivers: enriched dice roll display, Misery tracker in header, injury/status indicators in character drawer, equipment condition visibility, end-of-game states (death and 7th Misery), and session restart flow.

</domain>

<decisions>
## Implementation Decisions

### Dice roll display
- **D-01:** Enriched callout — extend existing `ToolResultCallout` with structured breakdown showing formula, modifiers, target, and outcome (e.g., "d20+STR(+2)=14 vs DR 12 — HIT"). Keeps current inline position within narrator messages.
- **D-02:** Backend structured data — backend sends richer `tool_result` payload for dice calls with structured fields (roll, formula, target, outcome) rather than raw result strings. Frontend renders, doesn't parse.
- **D-03:** Dice-only enrichment — only dice/roll tool results get the enriched breakdown. Other tool results keep current generic `ToolResultCallout` format.

### Misery tracker
- **D-04:** Header bar placement — compact doom clock with 7 small pips in the header next to HP indicator. Always visible as a constant reminder of approaching apocalypse. Triggered miseries glow pink, empty slots are ash/dim.
- **D-05:** Subtle pulse animation — newly filled Misery slot glows/pulses once in pink, then settles. Consistent with Phase 5's "silent updates" philosophy — the narrator's text carries the drama.
- **D-06:** Data already available — `StateUpdateEvent` already carries `miseryCount`. Frontend just needs to render it. No backend changes needed for Misery tracker itself.

### Injury & status indicators
- **D-07:** Icon badges in character drawer — small icon/glyph badges for each injury type (LostEye, StabbedLung, BrokenHand, CrushedFoot, SeveredArm, SmashedFace). Active injuries shown in pink, inactive injuries hidden entirely. Separate "STATUS" section for infection, encumbrance, dizzy-from-magic.
- **D-08:** Backend StateUpdateEvent enrichment — `StateUpdateEvent` must be extended with injury flags (hasLostEye, hasStabbedLung, etc.), infection, encumbrance, dizzy-from-magic, isDead, armorTier, hasShield, isShieldBroken. Frontend `CharacterData` type and Zustand store updated to match.
- **D-09:** Header stays minimal — header shows HP bar + Misery pips only. Injury and equipment details live exclusively in the character drawer. No injury badges or counts in the header.

### Equipment condition
- **D-10:** Tier + condition display — show armor tier (Light/Medium/Heavy) with visual indicator. Broken shield shown as struck-through or dimmed. Extends existing `EquipmentSlot` component. Shield added as a new equipment row when present.

### Session lifecycle & end states
- **D-11:** Narrator farewell + end card — when character dies or 7th Misery fires, narrator delivers final message in chat, then a styled end card appears over the chat showing: character name, cause of death/doom, session stats (days survived, miseries witnessed), and "Begin Anew" button.
- **D-12:** Same structure, different text — death and apocalypse use the same end card layout. Title changes: "YOUR WRETCH HAS FALLEN" (death, pink accent) vs "THE WORLD HAS ENDED" (7th Misery, yellow accent). Narrator text handles the narrative difference.
- **D-13:** Quick restart — "Begin Anew" creates a new session immediately and drops into character creation. Frictionless restart matches Mork Borg's high lethality.
- **D-14:** Ended sessions viewable — player can open ended sessions from the session list in read-only mode (scrollable history, input disabled, end card shown). Sessions marked with death/doom indicator in session list.

### Claude's Discretion
- Exact injury icon/glyph choices (emoji, SVG, or CSS icons)
- Misery pip styling details (size, spacing, glow effect implementation)
- End card typography and layout within the doom aesthetic
- Dice callout internal layout (stacked vs inline formula display)
- Armor tier visual indicator style (bar segments, text label, or icon)
- Session list indicator style for ended sessions
- Animation timing for Misery pulse and end card appearance
- How to detect "cause of death" from backend state for end card stats

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements
- `.planning/REQUIREMENTS.md` — MORK-01, MORK-02, MORK-03, CHAR-03, CHAR-04 are this phase's requirements

### Prior phase context
- `.planning/phases/05-core-gameplay-interface/05-CONTEXT.md` — Character drawer pattern, SSE state_update handling, header HP indicator, silent update philosophy
- `.planning/phases/04-frontend-foundation-and-character-creation/04-CONTEXT.md` — Doom aesthetic decisions, ToolResultCallout patterns, SSE event types, color palette

### Domain models
- `WrtechedWhispers/WretchedWhispers.Core/Characters/Status/InjuryKind.cs` — Flags enum defining 6 injury types
- `WrtechedWhispers/WretchedWhispers.Core/Characters/Status/InjurySet.cs` — Injury value object with penalty methods
- `WrtechedWhispers/WretchedWhispers.Core/Campaigns/World/CalendarOfNechrubel.cs` — Misery calendar with WorldEnded flag and DawnRoll
- `WrtechedWhispers/WretchedWhispers.Core/Campaigns/World/Misery.cs` — Misery entity (Code + Psalm)
- `WrtechedWhispers/WretchedWhispers.Semantic/Models/CharacterDto.cs` — Full character DTO with injuries, equipment, status flags

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `wretched-whispers-web/src/components/chat/ToolResultCallout.tsx` — Already renders dice rolls with "FATE DECIDES" header and yellow styling; `isDiceFunction()` detection exists. Extend for structured breakdown.
- `wretched-whispers-web/src/components/character/CharacterDrawer.tsx` — Full character drawer with sections for HP, abilities, equipment, inventory. Add injury/status section.
- `wretched-whispers-web/src/components/character/EquipmentSlot.tsx` — Equipment display component. Extend for armor tier and shield condition.
- `wretched-whispers-web/src/components/character/HpBar.tsx` — HP bar with `variant="full"` and `variant="compact"` modes.
- `wretched-whispers-web/src/components/layout/Header.tsx` — Fixed header with HP indicator and character sheet button. Add Misery pips here.
- `wretched-whispers-web/src/stores/sessionStore.ts` — Zustand store with `setStateUpdate()` that maps `StateUpdateEvent` to `CharacterData`. Extend `CharacterData` type and mapping.
- `wretched-whispers-web/src/types/api.ts` — `StateUpdateEvent` already has `miseryCount` field. `CharacterData` interface needs injury/equipment/status fields.

### Established Patterns
- Zustand granular selectors to minimize re-renders
- Silent state updates via SSE — UI reflects new state without toasts/popups
- Doom palette: yellow #ffe000, pink #ff1493, bone #e8e0d4, ash #8a8a8a, blood #8b0000
- Cinzel for display headers, Inter for body text
- `apiFetch` wrapper for authenticated API calls
- AbortController for SSE cleanup
- Focus trap pattern in drawer (manual, no library)

### Integration Points
- `StateUpdateEvent` (SSE) — needs enrichment with injury flags, equipment condition, isDead
- `tool_result` (SSE) — dice tool results need structured payload from backend (formula, target, outcome)
- `GET /sessions/{id}` — SessionDetailDto may need character injury/equipment fields for initial load
- Session status `"ended"` — already exists in status enum, drives end-of-game UI transition
- `POST /sessions` — used by "Begin Anew" to create new session after death/doom
- `CalendarOfNechrubel.WorldEnded` — backend flag for 7th Misery detection
- `Character.IsDead` — backend flag for character death detection

</code_context>

<specifics>
## Specific Ideas

- Dice callout should feel like revealing fate — the structured breakdown makes mechanical outcomes exciting rather than opaque
- Misery pips in the header create a persistent sense of dread without being intrusive
- Injuries should look like battle scars, not a medical chart — icon badges with doom aesthetic
- The end card should feel like a tombstone or final page of a book — dignified, atmospheric, in-theme
- Quick restart is essential for Mork Borg's philosophy: death is frequent, cheap, and expected

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-mechanical-visibility-and-session-lifecycle*
*Context gathered: 2026-03-24*
