---
phase: 06-mechanical-visibility-and-session-lifecycle
verified: 2026-03-24T15:30:00Z
status: passed
score: 11/11 must-haves verified
re_verification: false
---

# Phase 06: Mechanical Visibility and Session Lifecycle — Verification Report

**Phase Goal:** Players can see the real dice rolls and mechanical outcomes behind the narrative, track the world's doom, monitor their character's physical state, and play through a complete Mork Borg session from creation to death or apocalypse
**Verified:** 2026-03-24T15:30:00Z
**Status:** PASSED
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                                                    | Status     | Evidence                                                                                                             |
|----|--------------------------------------------------------------------------------------------------------------------------|------------|----------------------------------------------------------------------------------------------------------------------|
| 1  | DicePlugin.Roll returns a structured object with formula and numeric result                                               | VERIFIED   | `DicePlugin.cs` returns `DiceRollResult(Formula, Result)` record; 3 tests pass                                       |
| 2  | StateUpdateEvent SSE payload includes injury flags, equipment condition, isDead, and worldEnded                          | VERIFIED   | `GameSessionService.cs` lines 178-270 define and emit all 14 new fields in the anonymous SSE object                  |
| 3  | SessionDetailDto REST response includes injury/equipment/status fields for initial load                                  | VERIFIED   | `SessionDetailDto.cs` has 14 nullable parameters (`CharacterHasLostEye`…`WorldEnded`); `SessionEndpoints.cs` passes all  |
| 4  | DeriveStatus returns "ended" when character is dead or world has ended                                                   | VERIFIED   | `DeriveStatusTests.cs` has 5 tests covering all 3 branches; 252/252 tests pass                                       |
| 5  | 7 Misery pips are visible in the header next to HP indicator                                                             | VERIFIED   | `MiseryTracker.tsx` renders 7 pips; `CharacterDrawerToggle.tsx` imports and renders `<MiseryTracker count={miseryCount} />` |
| 6  | Dice rolls display with formula and result breakdown in the enriched callout                                             | VERIFIED   | `ToolResultCallout.tsx` contains `isDiceRollData` type guard; renders `diceData.formula` and `= {diceData.result}` when structured |
| 7  | Active injuries appear as pink icon badges in the character drawer                                                       | VERIFIED   | `InjuryBadges.tsx` with 6-entry INJURIES map; `CharacterDrawer.tsx` renders conditionally when any injury flag is true |
| 8  | Status effects (infected, dizzy, encumbered) display in a separate STATUS section                                        | VERIFIED   | `StatusIndicators.tsx` with STATUSES map; `CharacterDrawer.tsx` renders conditionally when any status flag is true    |
| 9  | Armor tier shows visual indicator squares; broken shield is struck-through                                               | VERIFIED   | `EquipmentSlot.tsx` renders tier squares (`\u25A0` × 1/2/3); applies `line-through` when `isBroken===true`; `CharacterDrawer.tsx` passes `tier` and shield slot |
| 10 | When character dies or 7th Misery fires, an end card overlay shows the correct variant with Begin Anew button           | VERIFIED   | `EndCard.tsx` contains `"YOUR WRETCH HAS FALLEN"` / `"THE WORLD HAS ENDED"` titles; `"BEGIN ANEW"` button; `worldEnded` priority logic |
| 11 | Ended sessions show read-only chat with disabled input and skull indicator in session list                               | VERIFIED   | `ChatInput.tsx` checks `isEnded`, shows `"This tale has ended..."`, hides SEND button; `SessionCard.tsx` uses `"\u2620 Ended"` and `opacity-75` |

**Score:** 11/11 truths verified

---

### Required Artifacts

| Artifact                                                                                 | Expected                                           | Status      | Details                                                                              |
|------------------------------------------------------------------------------------------|----------------------------------------------------|-------------|--------------------------------------------------------------------------------------|
| `WrtechedWhispers/WretchedWhispers.Semantic/DicePlugin.cs`                               | DiceRollResult record + structured Roll return     | VERIFIED    | `record DiceRollResult(string Formula, int Result)` with `[Description]` attributes  |
| `WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs`                   | Enriched SSE with 14 new fields                    | VERIFIED    | `hasLostEye`, all 6 injuries, 3 status, `isDead`, `armorTier`, `hasShield`, `isShieldBroken`, `worldEnded` present |
| `WrtechedWhispers/WretchedWhispers.Api/Models/SessionDetailDto.cs`                       | 14 nullable parameters added                       | VERIFIED    | `CharacterHasShield`, `CharacterIsShieldBroken`, `WorldEnded` all present             |
| `WrtechedWhispers/WretchedWhispers.Tests/DicePluginTests.cs`                             | 3 tests for DiceRollResult                         | VERIFIED    | 3 test methods; all pass                                                              |
| `WrtechedWhispers/WretchedWhispers.Tests/Campaigns/DeriveStatusTests.cs`                 | 5 tests for DeriveStatus end-state logic           | VERIFIED    | 5 test methods covering all 3 status branches; all pass                               |
| `wretched-whispers-web/src/components/character/MiseryTracker.tsx`                       | 7-pip doom clock component                         | VERIFIED    | Renders 7 pips, `aria-label`, `useRef` for prevCount tracking, pulse animation       |
| `wretched-whispers-web/src/components/character/InjuryBadges.tsx`                        | Injury icon badges component                       | VERIFIED    | All 6 injuries with unicode glyphs; returns null when none active                    |
| `wretched-whispers-web/src/components/character/StatusIndicators.tsx`                    | Status effects display component                   | VERIFIED    | INFECTED, ARCANE DAZE, ENCUMBERED entries; returns null when none active             |
| `wretched-whispers-web/src/components/session/EndCard.tsx`                               | End-of-game overlay component                      | VERIFIED    | Both title variants, BEGIN ANEW, SUMMONING..., error text, `role="dialog"`, `aria-modal` |
| `wretched-whispers-web/src/components/chat/ChatInput.tsx`                                | Disabled input for ended sessions                  | VERIFIED    | `"This tale has ended..."` placeholder; SEND button conditionally hidden             |

---

### Key Link Verification

| From                            | To                                              | Via                                     | Status  | Details                                                                                 |
|---------------------------------|-------------------------------------------------|-----------------------------------------|---------|-----------------------------------------------------------------------------------------|
| `DicePlugin.cs`                 | Semantic Kernel function result serialization   | `KernelFunction` return type            | WIRED   | `Roll()` returns `DiceRollResult`; no `int` return path remains                         |
| `GameSessionService.cs`         | Character entity injury/equipment properties    | Direct property access                  | WIRED   | `character.HasLostEye`, `HasStabbedLung`, `HasBrokenHand` etc. assigned from entity    |
| `sessionStore.ts`               | `types/api.ts`                                  | `setStateUpdate` mapping                | WIRED   | All `update.hasLostEye` → `characterData.hasLostEye` mappings present                  |
| `MiseryTracker.tsx`             | `sessionStore.ts`                               | `useSessionStore` selector              | WIRED   | `const miseryCount = useSessionStore((s) => s.miseryCount)` in `CharacterDrawerToggle` |
| `CharacterDrawer.tsx`           | `InjuryBadges.tsx`                              | Component render with characterData     | WIRED   | `<InjuryBadges characterData={characterData} />` inside conditional block               |
| `GameSessionPage (page.tsx)`    | `EndCard.tsx`                                   | Conditional render on `status === ended` | WIRED  | `{showEndCard && characterData && <EndCard ... />}` where `showEndCard = status === "ended" && !isStreaming` |
| `EndCard.tsx`                   | `POST /sessions`                                | `apiFetch` in `handleRestart`           | WIRED   | `apiFetch("/sessions", { method: "POST" })` + `router.push` in `handleRestart`          |
| `ChatInput.tsx`                 | `sessionStore` status                           | `status` prop                           | WIRED   | `const isEnded = status === "ended"` drives disabled state and SEND button visibility   |

---

### Data-Flow Trace (Level 4)

| Artifact                    | Data Variable   | Source                                    | Produces Real Data | Status      |
|-----------------------------|-----------------|-------------------------------------------|--------------------|-------------|
| `MiseryTracker.tsx`         | `count` prop    | `sessionStore.miseryCount` ← SSE `state_update.miseryCount` ← `updatedCampaign.Miseries.Count` | Yes — domain entity count | FLOWING |
| `InjuryBadges.tsx`          | `characterData` | `sessionStore.characterData` ← SSE `hasLostEye` etc. ← `character.HasLostEye` | Yes — domain entity properties | FLOWING |
| `ToolResultCallout.tsx`     | `toolResult.result` | SSE `tool_result` event carrying serialized `DiceRollResult` from Semantic Kernel | Yes — real dice roll | FLOWING |
| `EndCard.tsx`               | `isDead`, `worldEnded` | `page.tsx` reads `characterData.isDead` and `worldEnded` from store ← SSE ← `character.IsDead` / `campaign.WorldEnded` | Yes — domain entity state | FLOWING |

---

### Behavioral Spot-Checks

| Behavior                                    | Command                                                                                       | Result                          | Status  |
|---------------------------------------------|-----------------------------------------------------------------------------------------------|---------------------------------|---------|
| DicePlugin tests pass (structured return)   | `dotnet test --filter "DicePlugin" -v q`                                                      | 3/3 passed                      | PASS    |
| DeriveStatus tests pass (all 3 branches)    | `dotnet test --filter "DeriveStatus" -v q`                                                    | 5/5 passed                      | PASS    |
| Full backend test suite clean               | `dotnet test WrtechedWhispers.sln -v q`                                                       | 252/252 passed                  | PASS    |
| Frontend TypeScript compiles clean          | `npx tsc --noEmit`                                                                            | No errors                       | PASS    |
| Next.js production build succeeds           | `npx next build`                                                                              | All 7 routes compiled           | PASS    |

---

### Requirements Coverage

| Requirement | Source Plan | Description                                                              | Status      | Evidence                                                                            |
|-------------|-------------|--------------------------------------------------------------------------|-------------|------------------------------------------------------------------------------------|
| MORK-01     | 06-01, 06-03 | Full session lifecycle from character creation through 7th Misery or death | SATISFIED  | EndCard overlay (death/apocalypse variants), Begin Anew restart, read-only ended sessions; DeriveStatus "ended" branch verified |
| MORK-02     | 06-02        | Visual Misery tracker showing doom clock progress (7 slots)               | SATISFIED   | `MiseryTracker.tsx` renders 7 pips; wired to store `miseryCount`; visible in header |
| MORK-03     | 06-01, 06-02 | Visible dice rolls and mechanical outcomes alongside narrative             | SATISFIED   | `DiceRollResult` backend type; `isDiceRollData` type guard; enriched callout renders formula + result |
| CHAR-03     | 06-01, 06-02 | Visual injury/status indicators (broken limbs, infection, severed parts)  | SATISFIED   | `InjuryBadges.tsx` (6 injuries) + `StatusIndicators.tsx` (3 effects); wired into `CharacterDrawer.tsx` |
| CHAR-04     | 06-01, 06-02 | Equipment condition visible (armor degradation, weapon state)             | SATISFIED   | `EquipmentSlot.tsx` with tier squares and `line-through` for broken; `armorTier` + `hasShield`/`isShieldBroken` fields flow end-to-end |

All 5 requirements from REQUIREMENTS.md mapped to Phase 6 are satisfied. No orphaned requirements detected.

---

### Anti-Patterns Found

No blocker or warning-level anti-patterns found.

| File                          | Pattern Checked                      | Finding                                |
|-------------------------------|--------------------------------------|----------------------------------------|
| `MiseryTracker.tsx`           | Placeholder / stub return            | None — renders 7 pips with real data   |
| `InjuryBadges.tsx`            | `return null` as stub                | Intentional: null only when no injuries active (correct behavior) |
| `StatusIndicators.tsx`        | `return null` as stub                | Intentional: null only when no statuses active (correct behavior) |
| `EndCard.tsx`                 | Empty handler stub                   | None — `handleRestart` calls real `apiFetch` |
| `ChatInput.tsx`               | `onClick={() => {}}` stub            | None — properly disabled, not fake     |
| `GameSessionService.cs`       | Hardcoded empty SSE payload          | None — all 14 fields populated from entity properties |

---

### Human Verification Required

#### 1. Dice Roll Callout Display

**Test:** Play a session to the point where the narrator triggers a dice roll (e.g., attack, morale check). Observe the chat window.
**Expected:** A "FATE DECIDES" callout appears showing the dice expression (e.g., `d20`) on one line and the numeric result prominently on the next line (e.g., `= 14`). No raw JSON visible.
**Why human:** Requires a live LLM call and real SSE streaming; cannot be verified statically.

#### 2. Misery Pip Pulse Animation

**Test:** During an active session, trigger a Misery (dawn roll). Observe the header.
**Expected:** The newly filled Misery pip plays a brief pulse animation. Previously filled pips do not re-animate.
**Why human:** CSS animation behavior requires visual observation in a running browser; `doom-pulse` keyframe effect cannot be verified statically.

#### 3. End Card Timing Gate

**Test:** Force a session to the "ended" status while a narrator message is streaming. Observe behavior.
**Expected:** The end card overlay does NOT appear until the narrator's farewell message finishes streaming completely.
**Why human:** Requires real-time SSE observation to confirm `!isStreaming` gate works correctly in practice.

#### 4. Begin Anew Navigation Flow

**Test:** On an ended session's end card, click "BEGIN ANEW".
**Expected:** The button shows "SUMMONING..." briefly, then the page navigates to a fresh new session at `/sessions/{newId}` with a clean character-creation state.
**Why human:** Requires live backend, `POST /sessions` API call, and router navigation to verify the full flow.

---

### Gaps Summary

No gaps. All automated checks passed, all 11 observable truths are verified, and all 5 Phase 6 requirements are satisfied.

---

## Summary

Phase 06 delivered a complete mechanical visibility and session lifecycle system across 3 plans and all layers of the stack:

**Backend (Plan 01):** `DicePlugin` now returns a structured `DiceRollResult` record enabling formula display. The SSE `state_update` payload and `SessionDetailDto` REST response both carry 14 new fields covering all 6 injury flags, 3 status conditions, `isDead`, `armorTier`, `hasShield`, `isShieldBroken`, and `worldEnded`. A `Campaign.WorldEnded` public property was added to bridge the internal calendar. DeriveStatus logic is covered by 5 unit tests across all 3 status branches.

**Frontend mechanical visibility (Plan 02):** `MiseryTracker` (7-pip doom clock with pulse animation), `InjuryBadges` (6 injury glyphs), and `StatusIndicators` (3 status effects) were created. `ToolResultCallout` was enriched with a type guard to display structured dice data. `EquipmentSlot` gained tier indicator squares and broken-item strike-through. All components are wired into `CharacterDrawer` with correct section order, and `MiseryTracker` is visible in the header toggle.

**Session lifecycle (Plan 03):** `EndCard` overlay handles both death (pink, "YOUR WRETCH HAS FALLEN") and apocalypse (yellow, "THE WORLD HAS ENDED") variants with fade-in animation, a "BEGIN ANEW" restart button that calls `POST /sessions` and navigates to the new session, and proper streaming gate (`status === "ended" && !isStreaming`). `ChatInput` shows "This tale has ended..." with hidden SEND button. `SessionCard` shows skull glyph and reduced opacity for ended sessions.

---

_Verified: 2026-03-24T15:30:00Z_
_Verifier: Claude (gsd-verifier)_
