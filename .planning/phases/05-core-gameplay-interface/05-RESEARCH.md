# Phase 5: Core Gameplay Interface - Research

**Researched:** 2026-03-23
**Domain:** React gameplay UI (character sheet drawer, HP indicator, message pagination, state updates via SSE)
**Confidence:** HIGH

## Summary

Phase 5 transforms the existing chat interface from character-creation-only into a full gameplay experience. The codebase already has a solid foundation: SSE streaming with `@microsoft/fetch-event-source`, Zustand state management with streaming isolation, a chat window with auto-scroll, and paginated message endpoints on the backend. The primary work involves: (1) adding a character sheet drawer/overlay, (2) extending the Header with HP indicator and drawer toggle, (3) implementing load-more pagination for message history, (4) making the UI transition gracefully from character-creation to in-progress mode, and (5) extending both backend `state_update` SSE payloads and frontend stores to carry full character data (abilities, inventory, equipment).

The most significant gap is that the backend `state_update` SSE event currently only sends HP and basic campaign data -- it does NOT include abilities, inventory, weapon, or armor. Decision D-07 requires SSE-driven local state for the drawer. Either the `state_update` payload must be enriched on the backend, or a new `GET /sessions/{id}/character` endpoint is needed for initial load (with SSE updates for HP only). A hybrid approach is recommended: enrich `state_update` with full character snapshot AND add a character endpoint for initial session load.

**Primary recommendation:** Extend backend `state_update` to include full character data, add a `GET /sessions/{id}/character` endpoint, create a dedicated `characterStore` in Zustand, and build the drawer + HP indicator as new components that read from this store.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Overlay/drawer pattern -- no persistent sidebar. A button in the header opens a full-height slide-in drawer overlaying the chat. Chat stays full-width at all times.
- **D-02:** Essentials only -- show character name, HP bar, 4 abilities (STR/AGI/PRE/TOU), equipped weapon + armor, and inventory list. Compact and glanceable. Injury indicators and equipment condition deferred to Phase 6.
- **D-03:** Header HP indicator -- a compact HP readout (mini bar or "HP 6/8") visible in the header bar next to the character sheet button. Always visible without opening the drawer.
- **D-04:** Load-more button at the top of the chat -- "Load earlier messages" button fetches the next page from `GET /sessions/{id}/messages`. Explicit, no scroll-position surprises. No infinite scroll or eager loading.
- **D-05:** Subtle shift -- when session status changes from `character-creation` to `in-progress`, the character sheet button and HP bar fade into the header, and the input placeholder changes from "Speak, wretch..." to "What do you do?". No hard break or interstitial.
- **D-06:** Silent updates -- HP bar in header and drawer data update immediately when `state_update` SSE events arrive. No toast, popup, or flash animation.
- **D-07:** SSE-driven local state -- `state_update` events carry character data (HP, abilities, inventory). Zustand store updates locally. Drawer reads from store, no extra API fetch on open. Backend may need richer `state_update` payloads.

### Claude's Discretion
- Drawer slide-in animation direction and timing
- Character sheet layout within the drawer (sections, spacing, typography)
- HP bar visual style (segmented, continuous, color gradient)
- Load-more button styling and loading state
- How many messages to load per page
- Exact transition animation timing for header elements appearing
- State update store shape and selector design

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| GAME-01 | User can type free-text actions for their character | ChatInput already exists; needs conditional placeholder based on session status (D-05) |
| GAME-02 | Narrator responses stream word-by-word as generated | Already implemented via useSseStream + NarratorMessage streaming; no changes needed |
| GAME-03 | User can scroll back through message history | Load-more button at top of ChatWindow using `GET /sessions/{id}/messages?page=N` (D-04) |
| GAME-04 | Message history persists across sessions | Backend already persists messages in SQLite; frontend loads via setSession on mount; load-more fetches older pages |
| CHAR-02 | Character sheet sidebar displays HP, abilities, inventory, armor | New CharacterDrawer component, Header HP indicator, characterStore, backend state_update enrichment (D-01 through D-07) |
</phase_requirements>

## Standard Stack

### Core (already installed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| React | 19.2.3 | UI framework | Already in project |
| Next.js | 16.1.6 | App router, SSR | Already in project |
| Zustand | 5.0.11 | State management | Already in project, granular selector pattern established |
| @microsoft/fetch-event-source | 2.0.1 | SSE streaming | Already in project, used by useSseStream |
| Tailwind CSS | 4.x | Styling | Already in project, doom color palette defined |

### No New Dependencies Required
This phase requires no new npm packages. All UI work uses existing React, Tailwind, and Zustand. CSS transitions handle animations. The drawer, HP bar, and load-more button are all straightforward component work.

## Architecture Patterns

### Component Structure
```
src/
  components/
    character/
      CharacterDrawer.tsx     # Full-height slide-in overlay with character data
      HpBar.tsx               # Reusable HP bar component (used in drawer AND header)
      AbilityDisplay.tsx      # Single ability score display (STR +1, etc.)
      InventoryList.tsx       # Scrollable inventory item list
    chat/
      ChatWindow.tsx          # MODIFY: add load-more button at top
      ChatInput.tsx           # MODIFY: conditional placeholder based on session status
      LoadMoreButton.tsx      # "Load earlier messages" button with loading state
    layout/
      Header.tsx              # MODIFY: add HP indicator + character sheet toggle
  stores/
    characterStore.ts         # NEW: dedicated store for character state from SSE
    sessionStore.ts           # MODIFY: extend setStateUpdate, add pagination state
  hooks/
    useSseStream.ts           # MODIFY: route enriched state_update to characterStore
  types/
    api.ts                    # MODIFY: extend StateUpdateEvent with character data
```

### Pattern 1: Dedicated Character Store (Zustand)
**What:** Separate Zustand store for character data, following the project's established pattern of granular stores (authStore, sessionStore).
**When to use:** Character data changes independently from chat messages and session metadata. Isolating it prevents unnecessary re-renders of the chat when HP changes.
**Example:**
```typescript
// src/stores/characterStore.ts
import { create } from "zustand";

interface CharacterState {
  characterId: string | null;
  name: string | null;
  currentHp: number | null;
  maxHp: number | null;
  abilities: {
    strength: number;
    agility: number;
    presence: number;
    toughness: number;
  } | null;
  weapon: { kind: string; damageDie: string } | null;
  armor: { tier: string } | null;
  inventory: Array<{
    id: string;
    description: string;
    isBulky: boolean;
    quantity: number;
  }>;

  // Actions
  setCharacter: (data: CharacterData) => void;
  updateFromSse: (update: CharacterSsePayload) => void;
  reset: () => void;
}
```

### Pattern 2: Drawer Overlay with CSS Transitions
**What:** The character sheet uses a fixed-position overlay that slides in from the right, using CSS `transform` and `transition` for smooth animation. A semi-transparent backdrop dismisses on click.
**When to use:** D-01 mandates overlay/drawer pattern.
**Example:**
```typescript
// Drawer container with translate-x transition
<div className={`fixed inset-y-0 right-0 z-50 w-80 bg-doom-dark border-l border-doom-card
  transform transition-transform duration-300 ease-in-out
  ${isOpen ? 'translate-x-0' : 'translate-x-full'}`}>
  {/* Character sheet content */}
</div>
// Backdrop
{isOpen && (
  <div className="fixed inset-0 z-40 bg-black/50" onClick={onClose} />
)}
```

### Pattern 3: Load-More Prepend with Scroll Position Preservation
**What:** When "Load earlier messages" is clicked, older messages are prepended to the list. The scroll position must be preserved so the user stays at the same message they were reading, not jumped to the top.
**When to use:** D-04 mandates explicit load-more button.
**Example:**
```typescript
// Before prepending, capture scroll height
const prevScrollHeight = containerRef.current.scrollHeight;

// After new messages render, restore position
requestAnimationFrame(() => {
  const newScrollHeight = containerRef.current.scrollHeight;
  containerRef.current.scrollTop = newScrollHeight - prevScrollHeight;
});
```

### Pattern 4: Conditional Header Elements with Status-Driven Transitions
**What:** Header HP indicator and character sheet button only appear when session status is `in-progress`. They fade in using CSS transition on opacity/transform when the status changes from `character-creation`.
**When to use:** D-05 mandates subtle transition.
**Example:**
```typescript
// In Header, conditionally render gameplay elements
const status = useSessionStore((s) => s.status);
const showGameplayUI = status === 'in-progress';

<div className={`flex items-center gap-2 transition-opacity duration-500
  ${showGameplayUI ? 'opacity-100' : 'opacity-0 pointer-events-none'}`}>
  <HpIndicator />
  <CharacterSheetButton onClick={() => setDrawerOpen(true)} />
</div>
```

### Anti-Patterns to Avoid
- **Fetching character on drawer open:** D-07 explicitly says "no extra API fetch on open" -- drawer reads from Zustand store populated by SSE events.
- **Re-rendering entire chat on HP change:** Character data MUST be in a separate store or separate selectors. HP changes every turn; chat messages should not re-render.
- **Infinite scroll for history:** D-04 explicitly rejects this. Use load-more button only.
- **Toast/popup on state changes:** D-06 explicitly says silent updates. No notifications for HP changes.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Scroll position preservation | Custom scroll manager with MutationObserver | `requestAnimationFrame` + `scrollHeight` delta approach | Simple, reliable, no external dependency needed |
| Drawer animation | Custom JS animation loop | CSS `transform` + `transition` | Hardware-accelerated, no JS overhead, matches project's Tailwind approach |
| SSE event routing | Custom event bus | Direct Zustand store updates in `useSseStream` switch | Pattern already established, consistent with existing code |

## Common Pitfalls

### Pitfall 1: State Update Payload Gap
**What goes wrong:** The backend `state_update` SSE event currently only sends `campaignId`, `currentDay`, `currentHour`, `characterId`, `characterHp`, `characterMaxHp`, `miseryCount`, `status`. It does NOT send abilities, inventory, weapon, or armor data.
**Why it happens:** The original implementation was minimal -- just enough for Phase 4's status tracking.
**How to avoid:** Backend must be modified in `GameSessionService.ExecuteAgentTurnAsync` to include character abilities, weapon, armor, and inventory in the `state_update` payload. This is a backend change that must happen before frontend can implement D-07.
**Warning signs:** Character drawer shows stale data or requires extra API calls.

### Pitfall 2: No Character GET Endpoint
**What goes wrong:** On initial session load (page refresh, returning to session), there's no way to fetch current character state. The `SessionDetailDto` only has `characterName`, `currentHp`, `maxHp` but NOT abilities, inventory, weapon, or armor.
**Why it happens:** Session detail was designed for the session list, not for the gameplay character sheet.
**How to avoid:** Add `GET /sessions/{id}/character` endpoint that returns full character data (name, HP, abilities, weapon, armor, inventory). Load this on session mount to populate the characterStore before any SSE events arrive.
**Warning signs:** Character drawer is empty on first load until player takes their first action and receives a state_update.

### Pitfall 3: Scroll Jump on Message Prepend
**What goes wrong:** When loading older messages and prepending them to the DOM, the scroll position jumps to show the newly added content at the top instead of keeping the user at their current position.
**Why it happens:** Adding DOM elements above the current scroll position increases scrollHeight, but scrollTop stays the same, so the viewport shifts.
**How to avoid:** Capture `scrollHeight` before prepend, then after React renders the new messages, set `scrollTop = newScrollHeight - prevScrollHeight`.
**Warning signs:** User reports being "thrown to the top" when clicking Load More.

### Pitfall 4: Message Page Number Off-By-One
**What goes wrong:** The backend uses 1-based page numbering (`page=1` is the most recent). Loading "earlier" messages means incrementing the page number. But the initial session load already fetches page 1, so load-more should start at page 2.
**Why it happens:** Confusion between "newest first" vs "oldest first" pagination and which page was already fetched.
**How to avoid:** Track `nextPage` in sessionStore, initialize to 2 after initial load. Also track `hasMoreMessages` by comparing `totalMessages` with loaded count.
**Warning signs:** Duplicate messages appearing, or first load-more returning the same messages.

### Pitfall 5: Placeholder Text Not Updating
**What goes wrong:** ChatInput hardcodes `placeholder="Speak, wretch..."`. When status transitions to `in-progress`, placeholder should change to "What do you do?" per D-05.
**Why it happens:** ChatInput doesn't currently receive or read session status.
**How to avoid:** Pass status as a prop to ChatInput (or have it read from sessionStore) and derive placeholder from status.
**Warning signs:** Placeholder stays "Speak, wretch..." even after character creation is complete.

## Code Examples

### Backend: Enriched state_update Payload
```csharp
// In GameSessionService.ExecuteAgentTurnAsync, replace the current state_update write:
writer.TryWrite(new SseEvent("state_update", new
{
    campaignId = updatedCampaign.Id,
    currentDay = updatedCampaign.CurrentDay,
    currentHour = updatedCampaign.CurrentHour,
    characterId,
    characterHp,
    characterMaxHp,
    miseryCount = updatedCampaign.Miseries.Count,
    status = DeriveStatus(updatedCampaign),
    // NEW: Full character data for frontend drawer
    characterName = character?.Name,
    abilities = character is not null ? new
    {
        strength = character.Abilities.Strength.Modifier,
        agility = character.Abilities.Agility.Modifier,
        presence = character.Abilities.Presence.Modifier,
        toughness = character.Abilities.Toughness.Modifier
    } : null,
    weapon = character?.Weapon is not null ? new
    {
        kind = character.Weapon.Kind.ToString(),
        damageDie = character.Weapon.DamageDie.ToString()
    } : null,
    armor = character?.Armor is not null ? new
    {
        tier = character.Armor.Tier.GetType().Name.Replace("ArmorTier", "")
    } : null,
    inventory = character?.Inventory?.InventoryItems?.Select(i => new
    {
        id = i.Id,
        description = i.Description,
        isBulky = i.IsBulky,
        quantity = i.Quantity
    }).ToArray()
}));
```

### Backend: Character GET Endpoint
```csharp
// In SessionEndpoints.cs, add to the group:
group.MapGet("/{sessionId:guid}/character", GetCharacter);

// Handler:
private static async Task<IResult> GetCharacter(
    Guid sessionId,
    HttpContext http,
    ICampaignsRepository campaignsRepo,
    ICharactersRepository charactersRepo)
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var campaign = await campaignsRepo.GetByOwner(sessionId, userId);
    if (campaign is null) return Results.NotFound();

    var playerId = campaign.Players.FirstOrDefault();
    if (playerId == Guid.Empty) return Results.NotFound();

    var character = await charactersRepo.Get(playerId);
    if (character is null) return Results.NotFound();

    return Results.Ok(new {
        characterId = character.Id,
        name = character.Name,
        currentHp = character.Hp.Current,
        maxHp = character.Hp.Max,
        abilities = new {
            strength = character.Abilities.Strength.Modifier,
            agility = character.Abilities.Agility.Modifier,
            presence = character.Abilities.Presence.Modifier,
            toughness = character.Abilities.Toughness.Modifier
        },
        weapon = new {
            kind = character.Weapon.Kind.ToString(),
            damageDie = character.Weapon.DamageDie.ToString()
        },
        armor = new {
            tier = character.Armor.Tier.GetType().Name.Replace("ArmorTier", "")
        },
        inventory = character.Inventory.InventoryItems.Select(i => new {
            id = i.Id,
            description = i.Description,
            isBulky = i.IsBulky,
            quantity = i.Quantity
        }).ToArray()
    });
}
```

### Frontend: Extended StateUpdateEvent Type
```typescript
// In types/api.ts
export interface StateUpdateEvent {
  campaignId: string;
  currentDay: number;
  currentHour: number;
  characterId?: string;
  characterHp?: number;
  characterMaxHp?: number;
  miseryCount: number;
  status: "character-creation" | "in-progress" | "ended";
  // Enriched character data (Phase 5)
  characterName?: string;
  abilities?: {
    strength: number;
    agility: number;
    presence: number;
    toughness: number;
  };
  weapon?: { kind: string; damageDie: string };
  armor?: { tier: string };
  inventory?: Array<{
    id: string;
    description: string;
    isBulky: boolean;
    quantity: number;
  }>;
}
```

### Frontend: Load-More Pagination
```typescript
// In sessionStore, add pagination state:
currentPage: number;        // Track which page we're on
totalMessages: number;      // From API response
hasMoreMessages: boolean;   // Computed: totalMessages > messages.length

// Load more action:
loadMoreMessages: async (sessionId: string) => {
  const state = get();
  const nextPage = state.currentPage + 1;
  const res = await apiFetch(`/sessions/${sessionId}/messages?page=${nextPage}`);
  const data = await res.json();
  // Prepend older messages to the front
  set({
    messages: [...data.messages.map(toMessage), ...state.messages],
    currentPage: nextPage,
    totalMessages: data.totalMessages,
    hasMoreMessages: /* computed */
  });
};
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Zustand 4 with middleware | Zustand 5 with `create` (no middleware needed for simple stores) | 2024 | Project already on v5 |
| React 18 useEffect patterns | React 19 `use()` for params unwrap | 2024 | Already using `use(params)` in session page |
| CSS-in-JS for animations | Tailwind `transition-*` utilities | Ongoing | Matches project style, no runtime cost |

## Open Questions

1. **Message ordering: newest-first or oldest-first on initial load?**
   - What we know: Backend paginates from index 0 (oldest) with skip/take. Page 1 returns the first N messages (oldest), not the most recent.
   - What's unclear: For gameplay, initial load should show the MOST RECENT messages. The current API returns oldest-first on page 1. Either the API needs a `?order=desc` parameter, or the frontend must calculate the last page number from `totalMessages`.
   - Recommendation: Modify the backend `GetSessionDetail` to return the LAST page of messages by default (most recent), and include `totalMessages` and `totalPages` in the response so the frontend knows where to paginate backward from. Alternatively, add `?latest=true` parameter.

2. **Backend ownership verification for character endpoint**
   - What we know: `campaignsRepo.GetByOwner(sessionId, userId)` pattern is established for session endpoints.
   - What's unclear: Whether `GetByOwner` exists or needs to be added vs. using the existing ownership check pattern.
   - Recommendation: Follow the same ownership pattern as other session endpoints. The existing `Get` + manual ownership check should work.

## Environment Availability

Step 2.6: SKIPPED (no external dependencies identified -- this phase is purely frontend component work plus backend endpoint additions, using already-installed tools).

## Sources

### Primary (HIGH confidence)
- **Codebase analysis** -- direct reading of all source files listed in research
- `wretched-whispers-web/src/stores/sessionStore.ts` -- current store shape and actions
- `wretched-whispers-web/src/hooks/useSseStream.ts` -- SSE event handling pattern
- `wretched-whispers-web/src/types/api.ts` -- current DTO types including StateUpdateEvent
- `WretchedWhispers.Api/Services/GameSessionService.cs` -- actual state_update payload (lines 180-190)
- `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` -- existing pagination endpoints
- `WretchedWhispers.Core/Characters/Character.cs` -- full character domain model

### Secondary (MEDIUM confidence)
- Zustand v5 patterns based on project's existing usage (authStore, sessionStore)
- CSS transition patterns based on Tailwind v4 utilities already in project

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries already in project, no new deps
- Architecture: HIGH -- patterns directly derived from existing codebase conventions
- Pitfalls: HIGH -- gaps identified by reading actual backend code (state_update payload, missing character endpoint)
- Backend changes: HIGH -- specific code locations and shapes identified from source

**Research date:** 2026-03-23
**Valid until:** 2026-04-23 (stable -- no external dependency changes expected)
