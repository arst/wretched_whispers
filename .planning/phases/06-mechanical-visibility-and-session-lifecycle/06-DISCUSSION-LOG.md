# Phase 6: Mechanical Visibility and Session Lifecycle - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-24
**Phase:** 06-mechanical-visibility-and-session-lifecycle
**Areas discussed:** Dice roll display, Misery tracker, Injury & status indicators, Session lifecycle & end states

---

## Dice Roll Display

| Option | Description | Selected |
|--------|-------------|----------|
| Enriched callout | Extend ToolResultCallout with structured breakdown (d20+STR(+2)=14 vs DR 12 — HIT). Inline within narrator messages. | ✓ |
| Side annotation | Dice results as margin notes next to narrative. Less intrusive but harder to implement. | |
| Expandable detail | Short inline result, click to expand full breakdown. | |

**User's choice:** Enriched callout
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Dice only | Only dice/roll tool results get enriched breakdown. Others keep generic format. | ✓ |
| All enriched | Every tool result type gets tailored visual treatment. | |
| Hide non-dice | Only show dice roll callouts, hide other tool results entirely. | |

**User's choice:** Dice only
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Backend structured | Backend sends richer payload for dice calls: { roll, formula, target, outcome }. Frontend renders. | ✓ |
| Frontend parsing | Frontend regex-parses existing result string. No backend changes but fragile. | |

**User's choice:** Backend structured
**Notes:** None

---

## Misery Tracker

| Option | Description | Selected |
|--------|-------------|----------|
| Header bar | Compact doom clock with 7 pips in header next to HP. Always visible. | ✓ |
| Character drawer | Misery tracker inside character sheet drawer. Less visible. | |
| Floating overlay | Fixed-position doom clock in corner. Always visible, separate from header. | |

**User's choice:** Header bar
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Subtle pulse | Newly filled slot glows/pulses once in pink, then settles. | ✓ |
| Screen flash | Brief screen-wide flash/shake when Misery triggers. | |
| Inline announcement | Styled banner in chat: "THE THIRD MISERY HAS FALLEN". | |

**User's choice:** Subtle pulse
**Notes:** None

---

## Injury & Status Indicators

| Option | Description | Selected |
|--------|-------------|----------|
| Icon badges | Small icon/glyph badges per injury type. Active injuries glow pink, inactive hidden. | ✓ |
| Text list | Simple text list of active injuries in drawer. | |
| Body silhouette | Character silhouette with highlighted injury locations. | |

**User's choice:** Icon badges
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Tier + condition | Show armor tier with visual indicator. Broken shield struck-through/dimmed. | ✓ |
| Name only | Just show equipment names. No tier or condition visibility. | |

**User's choice:** Tier + condition
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| HP + Misery only | Header stays clean with HP bar and Misery pips only. Injuries in drawer. | ✓ |
| Add injury count | Header shows small injury count badge next to HP. | |

**User's choice:** HP + Misery only
**Notes:** None

---

## Session Lifecycle & End States

| Option | Description | Selected |
|--------|-------------|----------|
| Narrator farewell + end card | Final narrator message, then styled end card with stats and "Begin Anew" button. | ✓ |
| Chat continues, input disabled | Input disabled after final words, session list button prominent. No overlay. | |
| Full-screen death scene | Dark overlay with dramatic typography. Different for death vs apocalypse. | |

**User's choice:** Narrator farewell + end card
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Same structure, different text | Same end card layout for both. Title/color changes: pink for death, yellow for doom. | ✓ |
| Distinct screens | Completely different end screens for death vs apocalypse. | |

**User's choice:** Same structure, different text
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| New session directly | "Begin Anew" creates new session immediately. Frictionless restart. | ✓ |
| Back to session list | Returns to session list to choose starting a new one. | |
| You decide | Claude picks best approach. | |

**User's choice:** New session directly
**Notes:** None

| Option | Description | Selected |
|--------|-------------|----------|
| Viewable read-only | Ended sessions viewable with scrollable history, disabled input, end card shown. | ✓ |
| Hidden after end | Ended sessions disappear or move to archive. | |

**User's choice:** Viewable read-only
**Notes:** None

---

## Claude's Discretion

- Exact injury icon/glyph choices
- Misery pip styling details
- End card typography and layout
- Dice callout internal layout
- Armor tier visual indicator style
- Session list indicator for ended sessions
- Animation timing details
- Cause-of-death detection from backend state

## Deferred Ideas

None — discussion stayed within phase scope
