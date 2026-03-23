# Phase 5: Core Gameplay Interface - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-23
**Phase:** 05-core-gameplay-interface
**Areas discussed:** Character sheet sidebar, Message history & pagination, Gameplay mode transition, State update handling

---

## Character Sheet Sidebar

### Q1: Where should the character sheet sidebar live relative to the chat?

| Option | Description | Selected |
|--------|-------------|----------|
| Right sidebar | Persistent panel to the right of chat on desktop. Collapses to toggle/drawer on mobile. | |
| Overlay/drawer | No persistent sidebar. Button opens full-height drawer overlaying chat. | ✓ |
| Collapsible bottom panel | Below chat input, collapsible panel shows stats. Compact bar always visible. | |

**User's choice:** Overlay/drawer
**Notes:** Maximizes chat space, keeps narrative as primary focus.

### Q2: What should the character sheet drawer show?

| Option | Description | Selected |
|--------|-------------|----------|
| Essentials only | Name, HP bar, 4 abilities, weapon + armor, inventory list. | ✓ |
| Full sheet | Everything above plus class/origin, gold, encumbrance, powers, status effects. | |
| You decide | Claude picks based on backend DTOs. | |

**User's choice:** Essentials only
**Notes:** Compact, glanceable. Phase 6 adds injury indicators and equipment condition.

### Q3: Should there be a compact HP indicator always visible?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — header bar HP | Small HP readout next to character name in header. Always visible. | ✓ |
| No — drawer only | Header stays minimal. Character info only in drawer. | |
| Subtle indicator on damage | No persistent HP, but flash notification on HP changes. | |

**User's choice:** Yes — header bar HP
**Notes:** HP bar in header provides glanceable health status without opening drawer.

---

## Message History & Pagination

### Q1: How should older messages load when scrolling back?

| Option | Description | Selected |
|--------|-------------|----------|
| Load-more button | "Load earlier messages" button at top of chat. Explicit, no surprises. | ✓ |
| Infinite scroll up | Auto-fetch on scroll to top. Seamless but can cause scroll jumps. | |
| Eager load all | Load entire history on session open. Simplest but may be slow. | |

**User's choice:** Load-more button
**Notes:** Simple, explicit, matches existing per-turn SSE pattern.

---

## Gameplay Mode Transition

### Q1: How should the UI change when character creation ends?

| Option | Description | Selected |
|--------|-------------|----------|
| Subtle shift | Sheet button and HP fade into header, placeholder changes. No hard break. | ✓ |
| Clean break | Dramatic interstitial, then clear chat and start fresh in gameplay mode. | |
| No visual change | Same UI throughout. Narrator handles transition narratively only. | |

**User's choice:** Subtle shift
**Notes:** Natural continuation of conversation. Narrator drives the transition narratively while UI elements appear smoothly.

---

## State Update Handling

### Q1: How should real-time character changes appear?

| Option | Description | Selected |
|--------|-------------|----------|
| Update header HP + drawer silently | HP bar and drawer update immediately. No toast/popup. | ✓ |
| Flash notification on change | Animated notification near HP bar on changes. | |
| Both — silent + flash on damage | Silent for most, red pulse on HP loss specifically. | |

**User's choice:** Update header HP + drawer silently
**Notes:** Narrator text already describes what happened. UI reflects state without competing for attention.

### Q2: Data source for character sheet drawer?

| Option | Description | Selected |
|--------|-------------|----------|
| SSE-driven local state | state_update events carry character data. Store updates locally. | ✓ |
| Fetch on drawer open | GET to /sessions/{id} on each drawer open. | |
| You decide | Claude picks based on current state_update event contents. | |

**User's choice:** SSE-driven local state
**Notes:** No extra API call on drawer open. Backend may need richer state_update payloads.

---

## Claude's Discretion

- Drawer animation direction and timing
- Character sheet layout within drawer
- HP bar visual style
- Load-more button styling
- Messages per page
- Transition animation timing
- State update store shape

## Deferred Ideas

None — discussion stayed within phase scope
