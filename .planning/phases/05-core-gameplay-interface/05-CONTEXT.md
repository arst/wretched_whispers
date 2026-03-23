# Phase 5: Core Gameplay Interface - Context

**Gathered:** 2026-03-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Players can play the game — type free-text actions, see streaming narrator responses word-by-word, scroll back through message history, and monitor their character's state via a character sheet drawer. This phase delivers: character sheet overlay/drawer, header HP indicator, message history pagination, gameplay mode transition from character creation, and real-time character state updates via SSE. Dice rolls, Misery tracker, injury indicators, and equipment condition are Phase 6 concerns.

</domain>

<decisions>
## Implementation Decisions

### Character sheet sidebar
- **D-01:** Overlay/drawer pattern — no persistent sidebar. A button in the header opens a full-height slide-in drawer overlaying the chat. Chat stays full-width at all times.
- **D-02:** Essentials only — show character name, HP bar, 4 abilities (STR/AGI/PRE/TOU), equipped weapon + armor, and inventory list. Compact and glanceable. Injury indicators and equipment condition deferred to Phase 6.
- **D-03:** Header HP indicator — a compact HP readout (mini bar or "HP 6/8") visible in the header bar next to the character sheet button. Always visible without opening the drawer.

### Message history & pagination
- **D-04:** Load-more button at the top of the chat — "Load earlier messages" button fetches the next page from `GET /sessions/{id}/messages`. Explicit, no scroll-position surprises. No infinite scroll or eager loading.

### Gameplay mode transition
- **D-05:** Subtle shift — when session status changes from `character-creation` to `in-progress`, the character sheet button and HP bar fade into the header, and the input placeholder changes from "Speak, wretch..." to "What do you do?". No hard break or interstitial — the conversation continues naturally, and the narrator handles the transition narratively.

### State update handling
- **D-06:** Silent updates — HP bar in header and drawer data update immediately when `state_update` SSE events arrive. No toast, popup, or flash animation. The narrator's text already describes what happened; UI just reflects the new state.
- **D-07:** SSE-driven local state — `state_update` events carry character data (HP, abilities, inventory). Zustand store updates locally. Drawer reads from store, no extra API fetch on open. Backend may need richer `state_update` payloads to support this.

### Claude's Discretion
- Drawer slide-in animation direction and timing
- Character sheet layout within the drawer (sections, spacing, typography)
- HP bar visual style (segmented, continuous, color gradient)
- Load-more button styling and loading state
- How many messages to load per page
- Exact transition animation timing for header elements appearing
- State update store shape and selector design

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

No external specs — requirements fully captured in decisions above and in:

### Requirements
- `.planning/REQUIREMENTS.md` — GAME-01, GAME-02, GAME-03, GAME-04, CHAR-02 are this phase's requirements

### Prior phase context
- `.planning/phases/04-frontend-foundation-and-character-creation/04-CONTEXT.md` — Doom aesthetic decisions, chat interface patterns, SSE event types, API integration points, component structure

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `src/components/chat/ChatWindow.tsx` — Message list container with auto-scroll; extend for load-more pagination
- `src/components/chat/ChatInput.tsx` — Player input with "Speak, wretch..." placeholder; needs conditional placeholder based on session status
- `src/components/chat/NarratorMessage.tsx` — Narrator message with streaming support and tool result callouts
- `src/components/chat/ThinkingIndicator.tsx` — Loading indicator during LLM processing
- `src/components/layout/Header.tsx` — Fixed top nav; needs HP indicator and character sheet button added
- `src/hooks/useSseStream.ts` — SSE streaming hook; already handles `state_update` events
- `src/stores/sessionStore.ts` — Has `setStateUpdate()` action; needs extension for character data
- `src/stores/authStore.ts` — Auth state with hydration pattern (reuse pattern for character store)
- `src/lib/api.ts` — `apiFetch` wrapper with token refresh; reuse for message pagination calls
- `src/types/api.ts` — DTOs including `SessionDetailDto`, `SessionPreviewDto` (has characterName, currentHp, maxHp)

### Established Patterns
- Zustand with granular selectors to minimize re-renders (e.g., `streamingText` separate from `messages`)
- `apiFetch` wrapper for all authenticated API calls with automatic token refresh
- Optimistic UI: player message added to store before API call
- AbortController pattern for SSE stream cleanup
- StoreHydration provider for SSR-safe store initialization
- Doom color palette: yellow #ffe000, pink #ff1493, bone #e8e0d4, ash #8a8a8a, blood #8b0000
- Cinzel for display headers, Inter for body text

### Integration Points
- `GET /sessions/{id}/messages?page=N` — paginated message history for load-more
- `state_update` SSE event — currently carries session status/time; needs to include character data (HP, abilities, inventory, equipment)
- `GET /sessions/{id}` — session detail DTO already has characterName, currentHp, maxHp for initial load
- Session status field (`character-creation` | `in-progress` | `ended`) drives gameplay mode transition

</code_context>

<specifics>
## Specific Ideas

- Chat should remain the primary focus — sidebar is an overlay, not a competing panel
- HP indicator in the header should be glanceable at a glance without opening the drawer
- The transition from character creation to gameplay should feel natural and narrator-driven, not jarring
- State changes should feel like they're part of the story, not UI notifications competing with the narrative

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-core-gameplay-interface*
*Context gathered: 2026-03-23*
