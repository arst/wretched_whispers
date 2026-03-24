# Phase 5: Core Gameplay Interface - Research

**Researched:** 2026-03-24
**Domain:** React/Next.js frontend -- character sheet drawer, message pagination, gameplay mode transition, SSE state updates
**Confidence:** HIGH

## Summary

Phase 5 extends the existing Phase 4 chat interface with four feature areas: (1) a character sheet drawer overlay triggered from the header, (2) load-more message pagination, (3) a gameplay mode transition when session status changes from `character-creation` to `in-progress`, and (4) real-time character state updates via SSE `state_update` events driving a Zustand store.

The existing codebase provides strong foundations. The session store, SSE streaming hook, chat components, and API client are all in place. The primary work is extending the store with `characterData`, building new character-focused components, modifying the header and chat window, and enriching the backend `state_update` payload to include abilities, inventory, and equipment data (currently only sends HP, campaignId, day/hour, miseryCount, status).

**Primary recommendation:** Extend the backend `state_update` SSE payload first (it currently lacks abilities, inventory, and equipment), then build the frontend character data store and components. The UI-SPEC at `05-UI-SPEC.md` provides complete visual contracts -- follow it exactly.

<user_constraints>

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Overlay/drawer pattern -- no persistent sidebar. A button in the header opens a full-height slide-in drawer overlaying the chat. Chat stays full-width at all times.
- **D-02:** Essentials only -- show character name, HP bar, 4 abilities (STR/AGI/PRE/TOU), equipped weapon + armor, and inventory list. Compact and glanceable. Injury indicators and equipment condition deferred to Phase 6.
- **D-03:** Header HP indicator -- a compact HP readout (mini bar or "HP 6/8") visible in the header bar next to the character sheet button. Always visible without opening the drawer.
- **D-04:** Load-more button at the top of the chat -- "Load earlier messages" button fetches the next page from `GET /sessions/{id}/messages`. Explicit, no scroll-position surprises. No infinite scroll or eager loading.
- **D-05:** Subtle shift -- when session status changes from `character-creation` to `in-progress`, the character sheet button and HP bar fade into the header, and the input placeholder changes. No hard break or interstitial.
- **D-06:** Silent updates -- HP bar in header and drawer data update immediately when `state_update` SSE events arrive. No toast, popup, or flash animation.
- **D-07:** SSE-driven local state -- `state_update` events carry character data. Zustand store updates locally. Drawer reads from store, no extra API fetch on open. Backend may need richer `state_update` payloads.

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
| GAME-01 | User can type free-text actions for their character | Already implemented in Phase 4 via ChatInput + useSseStream. Phase 5 needs conditional placeholder text based on session status. |
| GAME-02 | Narrator responses stream word-by-word as generated | Already implemented in Phase 4 via NarratorMessage + streaming store pattern. No changes needed. |
| GAME-03 | User can scroll back through message history | Requires LoadMoreButton component at top of ChatWindow, calling `GET /sessions/{id}/messages?page=N`, prepending older messages to store. |
| GAME-04 | Message history persists across sessions | Backend already persists messages in SQLite. Frontend loads via `GET /sessions/{id}` on mount (already done). Pagination extends this to older messages. |
| CHAR-02 | Character sheet sidebar displays HP, abilities, inventory, armor | Requires CharacterDrawer overlay, CharacterDrawerToggle in header, HpBar, AbilityScore, InventoryList, EquipmentSlot components. Backend state_update needs enrichment. |

</phase_requirements>

## Standard Stack

### Core (already installed)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Next.js | 16.1.6 | App framework | Already in use |
| React | 19.2.3 | UI library | Already in use |
| Zustand | 5.0.11 | State management | Already in use with granular selector pattern |
| Tailwind CSS | 4.x | Styling | Already in use with doom theme tokens |
| @microsoft/fetch-event-source | 2.0.1 | SSE streaming | Already in use for action streaming |

### Supporting
No new libraries needed. All Phase 5 features use existing stack.

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| CSS transitions for drawer | framer-motion | Overkill for a single slide-in; CSS transition-transform is sufficient and adds zero bundle |
| Manual focus trap | @headlessui/react Dialog | Adds dependency; manual implementation with useEffect + keydown listener is straightforward for one drawer |

**Installation:** No new packages required.

## Architecture Patterns

### New Component Structure
```
src/
  components/
    character/
      CharacterDrawer.tsx       # Full-height slide-in overlay
      CharacterDrawerToggle.tsx  # Header button + mini HP
      HpBar.tsx                 # Reusable HP bar (mini + full variants)
      AbilityScore.tsx          # Single ability (name + modifier)
      InventoryList.tsx         # Scrollable inventory items
      EquipmentSlot.tsx         # Weapon or armor display
    chat/
      LoadMoreButton.tsx        # "Load earlier messages" at top of chat
```

### Pattern 1: Zustand Store Extension for Character Data
**What:** Extend sessionStore with `characterData` field and `drawerOpen` boolean. State updates arrive via SSE and hydrate on session load.
**When to use:** Every time a `state_update` SSE event arrives or session detail loads.

Current `setStateUpdate` only sets `status`:
```typescript
setStateUpdate: (update) => set({ status: update.status }),
```

Must extend to also populate `characterData` from the enriched payload. Store shape per UI-SPEC:
```typescript
interface CharacterData {
  name: string;
  currentHp: number;
  maxHp: number;
  abilities: {
    strength: number;
    agility: number;
    presence: number;
    toughness: number;
  };
  weapon: string | null;
  armor: string | null;
  inventory: string[];
}

// Added to SessionState:
characterData: CharacterData | null;
drawerOpen: boolean;
toggleDrawer: () => void;
setCharacterData: (data: CharacterData) => void;
```

Use granular selectors following established pattern:
```typescript
const characterData = useSessionStore((s) => s.characterData);
const drawerOpen = useSessionStore((s) => s.drawerOpen);
```

### Pattern 2: Message Pagination (Prepend Pattern)
**What:** LoadMoreButton calls API, prepends older messages to the store array while preserving scroll position.
**When to use:** User clicks "Load earlier messages" at top of chat.

Key considerations:
- Backend `GET /sessions/{id}/messages?page=N&pageSize=20` returns paginated messages with `totalMessages` count
- Current `setSession()` replaces all messages; need a new `prependMessages(messages: ChatMessageDto[])` action
- Scroll position preservation: capture `scrollHeight` before prepend, restore after DOM update using `useLayoutEffect` or `requestAnimationFrame`
- Track pagination state: `currentPage`, `totalMessages`, `hasMoreMessages` in the store
- Default `pageSize` is 50 on backend; UI-SPEC says 20 per page -- pass `pageSize=20` in API call

Backend pagination note: The `GetSessionMessages` endpoint loads ALL chat history into memory then paginates in-memory with `Skip/Take`. This works for v1 session sizes but is not ideal. No change needed now.

### Pattern 3: Drawer Overlay with Focus Trap
**What:** Full-height right-edge drawer with backdrop, keyboard dismiss, and focus cycling.
**When to use:** User clicks CharacterDrawerToggle in header.

Implementation approach:
- CSS `translate-x-full` / `translate-x-0` transition with `duration-200 ease-out`
- Backdrop: fixed div with `bg-doom-black/60`, click to close
- Focus trap: `useEffect` on open that captures `document.activeElement`, adds keydown listener for Tab cycling and Escape close, restores focus on close
- ARIA: `role="dialog"`, `aria-modal="true"`, `aria-label="Character sheet"`
- Z-index: `z-50` (matches grain texture, but grain is `pointer-events:none` so no conflict)

### Pattern 4: Gameplay Mode Transition
**What:** When `state_update` SSE event changes status from `character-creation` to `in-progress`, header HP indicator fades in and input placeholder changes.
**When to use:** Automatic on status change.

- CharacterDrawerToggle: conditionally render with `opacity-0 -> opacity-100` CSS transition keyed on `status === 'in-progress'`
- ChatInput: receive status as prop or read from store, switch placeholder text
- No interstitial, no modal -- transition is narrator-driven per D-05

### Anti-Patterns to Avoid
- **Fetching character data on drawer open:** Per D-07, drawer reads from store. Never make an API call when opening the drawer.
- **Re-rendering entire message list on character data change:** Use granular Zustand selectors. Character data and messages must be independent slices.
- **Infinite scroll for message history:** Per D-04, use explicit load-more button only. No intersection observer, no scroll-triggered loading.
- **Toast/flash on state update:** Per D-06, state changes are silent. The narrator text describes what happened.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Scroll position preservation on prepend | Custom scroll management library | `scrollTop += newScrollHeight - oldScrollHeight` pattern after DOM mutation | Simple arithmetic; libraries add unnecessary abstraction |
| SSE event parsing | Custom EventSource wrapper | Existing `@microsoft/fetch-event-source` + `useSseStream` hook | Already working, battle-tested in Phase 4 |

**Key insight:** Phase 5 is almost entirely frontend extension work building on existing infrastructure. The only backend change is enriching the `state_update` SSE payload.

## Common Pitfalls

### Pitfall 1: Backend state_update Payload is Incomplete
**What goes wrong:** The frontend expects character name, abilities, inventory, weapon, and armor in `state_update` events, but the backend currently only sends `campaignId`, `currentDay`, `currentHour`, `characterId`, `characterHp`, `characterMaxHp`, `miseryCount`, `status`.
**Why it happens:** Phase 3 backend was built for campaign state tracking, not character sheet display.
**How to avoid:** Enrich the `state_update` payload in `GameSessionService.ExecuteAgentTurnAsync` (lines 160-191) to include character abilities, inventory items, weapon name, and armor name after loading the character entity. Also update the `StateUpdateEvent` TypeScript type.
**Warning signs:** Character drawer shows null/empty data despite SSE events arriving.

### Pitfall 2: SessionDetailDto Lacks Character Data for Initial Load
**What goes wrong:** When loading an existing `in-progress` session, the frontend needs character data immediately to populate the drawer, but `SessionDetailDto` only has top-level campaign fields -- no character abilities, inventory, or equipment.
**Why it happens:** The detail endpoint was designed for session overview, not full character display.
**How to avoid:** Either (a) enrich `SessionDetailDto` with character data fields, or (b) add a `GET /sessions/{id}/character` endpoint, or (c) rely on the first action's `state_update` event. Option (a) is simplest and aligns with D-07 ("no extra API fetch on open"). Add character fields to the response of `GetSessionDetail`.
**Warning signs:** Drawer is empty on page load until the player takes their first action.

### Pitfall 3: Scroll Position Jump on Message Prepend
**What goes wrong:** When older messages are prepended to the top of the list, the scroll position jumps to the top or shifts unexpectedly, disorienting the user.
**Why it happens:** Browser recalculates scroll position when DOM content is inserted above the current viewport.
**How to avoid:** Capture `containerRef.scrollHeight` before prepend, then after DOM update set `containerRef.scrollTop += containerRef.scrollHeight - previousScrollHeight`. Use `requestAnimationFrame` or `useLayoutEffect` to ensure measurement happens after render.
**Warning signs:** Chat jumps to top after clicking "Load earlier messages".

### Pitfall 4: Pagination Page Numbering Mismatch
**What goes wrong:** Frontend requests page 2 but gets the wrong set of messages because page 1 was loaded with different `pageSize`.
**Why it happens:** Backend `GetSessionMessages` uses `page` and `pageSize` parameters. `GetSessionDetail` defaults to `pageSize=50`, but UI-SPEC says 20 per load-more page.
**How to avoid:** On initial load, `GetSessionDetail` loads the latest page (page 1, pageSize 50). For load-more, use `GetSessionMessages` with `pageSize=20`. These are different endpoints with independent pagination. Calculate the correct page number based on `totalMessages` and messages already loaded, or switch to cursor/offset-based pagination.
**Warning signs:** Duplicate messages or gaps in history after load-more.

### Pitfall 5: Message Order for Pagination
**What goes wrong:** Backend returns messages in chronological order (oldest first by default), but load-more expects the most recent unseen messages.
**Why it happens:** Backend uses `Skip/Take` starting from the beginning of the chat history.
**How to avoid:** For load-more, you need messages BEFORE the currently loaded set. Calculate the correct page: if you have the latest 50 messages out of 120 total, the next load-more should fetch messages 51-70 (from the end). This means requesting the right offset. Consider passing `before` cursor or calculating: `page = Math.ceil((totalMessages - loadedCount) / pageSize)`.
**Warning signs:** Load-more shows the same messages or messages from the wrong part of history.

### Pitfall 6: Auto-scroll Interference with Load-More
**What goes wrong:** The existing `useAutoScroll` hook triggers scrollToBottom after load-more prepends messages, undoing the scroll position preservation.
**Why it happens:** `useAutoScroll` watches `messages.length` as a dependency and scrolls to bottom on change.
**How to avoid:** Add a guard: only auto-scroll when messages are appended (new message at end), not prepended (load-more at beginning). Track whether the last change was an append or prepend.
**Warning signs:** After loading older messages, chat snaps to the bottom.

## Code Examples

### Enriched state_update Backend Payload
```csharp
// In GameSessionService.ExecuteAgentTurnAsync, after loading character:
writer.TryWrite(new SseEvent("state_update", new
{
    campaignId = updatedCampaign.Id,
    currentDay = updatedCampaign.CurrentDay,
    currentHour = updatedCampaign.CurrentHour,
    characterId = character?.Id,
    characterName = character?.Name,
    characterHp = character?.Hp.Current,
    characterMaxHp = character?.Hp.Max,
    characterStrength = character?.Abilities.Strength.Modifier,
    characterAgility = character?.Abilities.Agility.Modifier,
    characterPresence = character?.Abilities.Presence.Modifier,
    characterToughness = character?.Abilities.Toughness.Modifier,
    characterWeapon = character?.Weapon?.Name,
    characterArmor = character?.Armor?.Name,
    characterInventory = character?.Inventory.InventoryItems
        .Select(i => i.Description).ToArray(),
    miseryCount = updatedCampaign.Miseries.Count,
    status = DeriveStatus(updatedCampaign)
}));
```

### Updated StateUpdateEvent TypeScript Type
```typescript
export interface StateUpdateEvent {
  campaignId: string;
  currentDay: number;
  currentHour: number;
  characterId?: string;
  characterName?: string;
  characterHp?: number;
  characterMaxHp?: number;
  characterStrength?: number;
  characterAgility?: number;
  characterPresence?: number;
  characterToughness?: number;
  characterWeapon?: string | null;
  characterArmor?: string | null;
  characterInventory?: string[];
  miseryCount: number;
  status: "character-creation" | "in-progress" | "ended";
}
```

### Store Extension for Character Data
```typescript
setStateUpdate: (update: StateUpdateEvent) => {
  const newState: Partial<SessionState> = { status: update.status };

  if (update.characterName && update.characterHp != null) {
    newState.characterData = {
      name: update.characterName,
      currentHp: update.characterHp,
      maxHp: update.characterMaxHp!,
      abilities: {
        strength: update.characterStrength ?? 0,
        agility: update.characterAgility ?? 0,
        presence: update.characterPresence ?? 0,
        toughness: update.characterToughness ?? 0,
      },
      weapon: update.characterWeapon ?? null,
      armor: update.characterArmor ?? null,
      inventory: update.characterInventory ?? [],
    };
  }

  set(newState);
},
```

### Scroll Position Preservation on Prepend
```typescript
const handleLoadMore = async () => {
  const container = containerRef.current;
  if (!container) return;

  const prevScrollHeight = container.scrollHeight;

  // Fetch and prepend messages via store action
  await loadOlderMessages();

  // Restore scroll position after DOM update
  requestAnimationFrame(() => {
    container.scrollTop += container.scrollHeight - prevScrollHeight;
  });
};
```

### Focus Trap for Drawer
```typescript
useEffect(() => {
  if (!isOpen) return;

  const previousFocus = document.activeElement as HTMLElement;
  const drawer = drawerRef.current;
  if (!drawer) return;

  const focusableElements = drawer.querySelectorAll<HTMLElement>(
    'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
  );
  const firstFocusable = focusableElements[0];
  const lastFocusable = focusableElements[focusableElements.length - 1];

  firstFocusable?.focus();

  const handleKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Escape') { onClose(); return; }
    if (e.key !== 'Tab') return;

    if (e.shiftKey && document.activeElement === firstFocusable) {
      e.preventDefault();
      lastFocusable?.focus();
    } else if (!e.shiftKey && document.activeElement === lastFocusable) {
      e.preventDefault();
      firstFocusable?.focus();
    }
  };

  document.addEventListener('keydown', handleKeyDown);
  return () => {
    document.removeEventListener('keydown', handleKeyDown);
    previousFocus?.focus();
  };
}, [isOpen, onClose]);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Separate character API call on drawer open | SSE-driven store with local reads (D-07) | This phase | Eliminates loading states in drawer; instant open |
| Infinite scroll for history | Explicit load-more button (D-04) | This phase | Simpler implementation, no scroll-position surprises |
| Sidebar layout with persistent panel | Overlay drawer (D-01) | This phase | Chat stays full-width; character sheet is secondary |

## Open Questions

1. **Backend Weapon/Armor Name Property**
   - What we know: `Character.Weapon` is type `Weapon`, `Character.Armor` is type `Armor`. These are domain objects, not simple strings.
   - What's unclear: Whether `Weapon` and `Armor` have a `Name` property or if we need `Kind.ToString()` or similar.
   - Recommendation: Check `Weapon.cs` and `Armor.cs` for display-friendly properties. If none exist, use `WeaponKind` or tier name. The planner should investigate these types.

2. **SessionDetailDto Character Enrichment**
   - What we know: Initial page load uses `GetSessionDetail` which returns no character abilities/inventory.
   - What's unclear: Whether to add character fields to `SessionDetailDto` or create a separate endpoint.
   - Recommendation: Add character data directly to `SessionDetailDto` (or a nested object). This aligns with D-07 -- no extra API fetch. The `ListSessions` endpoint already loads character name/HP, so the pattern exists.

3. **Pagination Direction**
   - What we know: Backend returns messages page 1 = oldest first. Load-more needs newest-loaded-so-far going backwards.
   - What's unclear: Whether to change backend pagination or handle reversal on frontend.
   - Recommendation: Simplest approach is to compute the correct page number on the frontend. If 120 total messages and 50 loaded (the latest), next load-more should request messages at the right offset. Alternatively, add a `before` parameter to the API.

## Environment Availability

Step 2.6: SKIPPED (no external dependencies identified). Phase 5 is purely code/config changes to existing Next.js frontend and .NET backend.

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection of all relevant source files
- `GameSessionService.cs` lines 160-191 -- current `state_update` payload shape
- `SessionEndpoints.cs` -- pagination endpoints and their signatures
- `sessionStore.ts` -- current store shape and actions
- `useSseStream.ts` -- current SSE event handling
- `Character.cs` -- domain entity with full property list
- `05-UI-SPEC.md` -- complete visual and interaction contracts
- `05-CONTEXT.md` -- locked user decisions

### Secondary (MEDIUM confidence)
- React 19 focus trap patterns -- based on standard DOM APIs, well-established
- Scroll position preservation technique -- widely documented browser behavior

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries already installed and in use
- Architecture: HIGH -- extending existing patterns with clear UI-SPEC contracts
- Pitfalls: HIGH -- identified through direct code inspection of backend payload gaps and pagination mechanics

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (stable -- extending existing codebase with no external dependencies)
