# Phase 6: Mechanical Visibility and Session Lifecycle - Research

**Researched:** 2026-03-24
**Domain:** Frontend UI enrichment (React/Next.js), backend SSE payload extension (.NET), session lifecycle management
**Confidence:** HIGH

## Summary

This phase extends an already-working game loop (chat, SSE streaming, character state) with **visibility** into the mechanical layer and **lifecycle management** for session endings. The backend domain model already has all the data needed (injuries, shield, armor tier, misery count, isDead, worldEnded). The primary work is: (1) enriching the `StateUpdateEvent` SSE payload and `SessionDetailDto` with injury/equipment/status fields, (2) enriching `tool_result` SSE payloads for dice rolls with structured breakdown data, (3) building new frontend components (MiseryTracker, InjuryBadges, EndCard), and (4) extending existing components (ToolResultCallout, CharacterDrawer, EquipmentSlot, Header, SessionCard, ChatInput).

The backend changes are straightforward data-plumbing: the `Character` entity already exposes `HasLostEye`, `HasStabbedLung`, `HasBrokenHand`, `HasCrushedFoot`, `HasSeveredArm`, `HasSmashedFace`, `IsInfected`, `IsDizzyFromMagic`, `IsEncumbered`, `IsDead`, `Shield?.IsBroken`, and `Armor.Tier`. These just need to be included in the SSE `state_update` and REST DTO payloads. The `DicePlugin.Roll` currently returns a bare `int`; it needs to return a structured object with formula, result, and optionally target/outcome context.

The frontend follows established patterns: Zustand store extension, Tailwind CSS with doom palette variables, granular selectors, silent state updates via SSE.

**Primary recommendation:** Backend-first approach -- enrich SSE payloads and DTOs first, then build frontend components that consume them. End card and session lifecycle changes come last since they depend on the "ended" status flowing correctly.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Enriched callout -- extend existing `ToolResultCallout` with structured breakdown showing formula, modifiers, target, and outcome (e.g., "d20+STR(+2)=14 vs DR 12 -- HIT"). Keeps current inline position within narrator messages.
- **D-02:** Backend structured data -- backend sends richer `tool_result` payload for dice calls with structured fields (roll, formula, target, outcome) rather than raw result strings. Frontend renders, doesn't parse.
- **D-03:** Dice-only enrichment -- only dice/roll tool results get the enriched breakdown. Other tool results keep current generic `ToolResultCallout` format.
- **D-04:** Header bar placement -- compact doom clock with 7 small pips in the header next to HP indicator. Always visible as a constant reminder of approaching apocalypse. Triggered miseries glow pink, empty slots are ash/dim.
- **D-05:** Subtle pulse animation -- newly filled Misery slot glows/pulses once in pink, then settles. Consistent with Phase 5's "silent updates" philosophy.
- **D-06:** Data already available -- `StateUpdateEvent` already carries `miseryCount`. Frontend just needs to render it. No backend changes needed for Misery tracker itself.
- **D-07:** Icon badges in character drawer -- small icon/glyph badges for each injury type. Active injuries shown in pink, inactive injuries hidden entirely. Separate "STATUS" section for infection, encumbrance, dizzy-from-magic.
- **D-08:** Backend StateUpdateEvent enrichment -- must extend with injury flags, equipment condition, isDead, armorTier, hasShield, isShieldBroken.
- **D-09:** Header stays minimal -- header shows HP bar + Misery pips only. No injury badges in header.
- **D-10:** Tier + condition display -- show armor tier with visual indicator. Broken shield shown as struck-through or dimmed. Extends `EquipmentSlot`.
- **D-11:** Narrator farewell + end card -- death or 7th Misery triggers narrator final message, then styled end card overlay.
- **D-12:** Same structure, different text -- death and apocalypse use same end card layout with different title/accent.
- **D-13:** Quick restart -- "Begin Anew" creates new session immediately via POST /sessions.
- **D-14:** Ended sessions viewable -- read-only mode with input disabled, end card shown, sessions marked in list.

### Claude's Discretion
- Exact injury icon/glyph choices (emoji, SVG, or CSS icons)
- Misery pip styling details (size, spacing, glow effect implementation)
- End card typography and layout within the doom aesthetic
- Dice callout internal layout (stacked vs inline formula display)
- Armor tier visual indicator style (bar segments, text label, or icon)
- Session list indicator style for ended sessions
- Animation timing for Misery pulse and end card appearance
- How to detect "cause of death" from backend state for end card stats

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| MORK-01 | Full session lifecycle from character creation through 7th Misery or death | Backend already has `Campaign.IsActive()`, `Character.IsDead`, `CalendarOfNechrubel.WorldEnded`. Status derived as "ended" when `!IsActive()`. Frontend needs end card overlay, read-only mode, restart flow. |
| MORK-02 | Visual Misery tracker showing doom clock progress (7 slots) | `StateUpdateEvent.miseryCount` already sent via SSE. Frontend needs MiseryTracker component in header with 7 pips. |
| MORK-03 | Visible dice rolls and mechanical outcomes alongside narrative | DicePlugin returns bare int. Needs structured return with formula/result. ToolResultCallout needs enriched rendering for dice functions. |
| CHAR-03 | Visual injury/status indicators (broken limbs, infection, severed parts) | Character entity has all 6 injury booleans, IsInfected, IsDizzyFromMagic, IsEncumbered. Need to add to StateUpdateEvent and CharacterData type. |
| CHAR-04 | Equipment condition visible (armor degradation, weapon state) | Character has Armor.Tier (NoArmor/Light/Medium/Heavy), Shield?.IsBroken. Need to add armorTier, hasShield, isShieldBroken to state payloads. |
</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Next.js | 15.x | Frontend framework | Already in project |
| React | 19.x | UI library | Already in project |
| Zustand | 5.x | State management | Already in project, established patterns |
| Tailwind CSS | 4.x | Styling | Already in project with doom palette |
| @microsoft/fetch-event-source | latest | SSE client | Already in project |
| .NET 9 | 9.0 | Backend API | Already in project |
| Microsoft.SemanticKernel | latest | AI orchestration | Already in project |

### No New Dependencies Required
This phase requires zero new libraries. All work extends existing components and patterns. The doom palette CSS variables already include all needed colors (yellow, pink, bone, ash, blood). Animations use CSS keyframes already established in globals.css.

## Architecture Patterns

### Backend Changes (Data Plumbing)

#### Pattern 1: StateUpdateEvent Enrichment
**What:** Add injury, equipment, and death fields to the anonymous object in `GameSessionService.ExecuteAgentTurnAsync`
**Current code (line ~205 in GameSessionService.cs):**
```csharp
writer.TryWrite(new SseEvent("state_update", new
{
    campaignId = updatedCampaign.Id,
    // ... existing fields ...
    miseryCount = updatedCampaign.Miseries.Count,
    status = DeriveStatus(updatedCampaign)
}));
```
**Add these fields:**
```csharp
// Injury flags (from Character entity)
hasLostEye = character?.HasLostEye ?? false,
hasStabbedLung = character?.HasStabbedLung ?? false,
hasBrokenHand = character?.HasBrokenHand ?? false,
hasCrushedFoot = character?.HasCrushedFoot ?? false,
hasSeveredArm = character?.HasSeveredArm ?? false,
hasSmashedFace = character?.HasSmashedFace ?? false,
// Status effects
isInfected = character?.IsInfected ?? false,
isDizzyFromMagic = character?.IsDizzyFromMagic ?? false,
isEncumbered = character?.IsEncumbered ?? false,
isDead = character?.IsDead ?? false,
// Equipment condition
armorTier = character?.Armor.Tier switch { ... },
hasShield = character?.Shield is not null,
isShieldBroken = character?.Shield?.IsBroken ?? false,
// World state
worldEnded = updatedCampaign.Calendar.WorldEnded,
```
**Confidence:** HIGH -- direct property access on existing entities.

#### Pattern 2: SessionDetailDto Enrichment
**What:** Add the same injury/equipment/status fields to `SessionDetailDto` record and `GetSessionDetail` endpoint
**Same pattern as StateUpdateEvent but for the REST response on session load.**
**Confidence:** HIGH -- follows identical pattern to how character fields were added previously.

#### Pattern 3: DicePlugin Structured Return
**What:** Change `DicePlugin.Roll` return type from `int` to a structured result object
**Current:**
```csharp
public int Roll(string dexExpression)
{
    var dex = DiceExpr.Parse(dexExpression);
    return dice.Roll(dex);
}
```
**New:**
```csharp
public DiceRollResult Roll(string dexExpression)
{
    var dex = DiceExpr.Parse(dexExpression);
    var result = dice.Roll(dex);
    return new DiceRollResult(dexExpression, result);
}

public record DiceRollResult(string Formula, int Result);
```
**Important:** The Semantic Kernel serializes the return value as JSON into `FunctionResultContent.Result`. The frontend already receives `result: unknown` in `ToolResultEvent`. The structured object will serialize as `{"formula":"d20","result":14}` instead of just `14`.
**Confidence:** HIGH -- Semantic Kernel handles serialization of return types automatically.

**Note on target/outcome context:** The DicePlugin only knows the formula and result. It does NOT know what the roll is for (attack vs DR, ability check, etc.). Target and outcome context comes from other plugins (Character.Challenge, Character.Attack). To get "vs DR 12 -- HIT" style display, we would need to either: (a) have the CharacterPlugin/EncounterPlugin also return structured results with context, or (b) accept that the dice callout shows only the roll formula and result without context. Option (b) is simpler and still satisfies MORK-03 ("visible dice rolls and mechanical outcomes"). The narrative text from the GM already explains what the roll was for.

### Frontend Changes

#### Pattern 4: CharacterData Type Extension
**What:** Extend the `CharacterData` interface in `types/api.ts` and update `StateUpdateEvent`
```typescript
export interface CharacterData {
  // ... existing fields ...
  // Injuries
  hasLostEye: boolean;
  hasStabbedLung: boolean;
  hasBrokenHand: boolean;
  hasCrushedFoot: boolean;
  hasSeveredArm: boolean;
  hasSmashedFace: boolean;
  // Status effects
  isInfected: boolean;
  isDizzyFromMagic: boolean;
  isEncumbered: boolean;
  isDead: boolean;
  // Equipment condition
  armorTier: string;
  hasShield: boolean;
  isShieldBroken: boolean;
}
```

#### Pattern 5: MiseryTracker Component
**What:** 7-pip doom clock in the header, next to HP indicator
**Where:** New component `MiseryTracker.tsx`, rendered in `CharacterDrawerToggle` or `Header`
**Rendering logic:** `miseryCount` is already in `StateUpdateEvent`. Store needs a `miseryCount` field (currently not stored -- `setStateUpdate` only maps character data and status).
**Key detail:** The `setStateUpdate` function in sessionStore currently ignores `miseryCount`. It must be stored in Zustand state so the MiseryTracker can read it.

#### Pattern 6: EndCard Overlay Component
**What:** Full-screen overlay that appears when `status === "ended"`
**Trigger:** `setStateUpdate` receives `status: "ended"` after the narrator's final message
**Content:** Character name, cause (death vs apocalypse -- distinguished by `isDead` vs `worldEnded`), "Begin Anew" button
**"Begin Anew":** Calls `POST /sessions` (same as session list "New Session" button), navigates to new session

#### Pattern 7: Read-Only Ended Sessions
**What:** When loading an ended session, show full chat history with input disabled and end card visible
**Where:** `GameSessionPage` already checks `status`. When `status === "ended"`, disable `ChatInput` and show end card.
**Session list:** `SessionCard` already has `ended` status styling (pink border). Can optionally add a skull/cross icon.

### Anti-Patterns to Avoid
- **Parsing dice results in frontend:** Decision D-02 explicitly says "Frontend renders, doesn't parse." Never regex-match dice notation from result strings.
- **Animating on every render:** Misery pulse should only fire on transition (miseryCount increases), not on every re-render. Use a `useRef` to track previous count.
- **Putting injuries in header:** Decision D-09 explicitly forbids injury badges in header. Injuries live exclusively in CharacterDrawer.
- **Blocking UI on end state:** The narrator's final message should finish streaming before the end card appears. Don't show end card mid-stream.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Animations | Custom JS animation timers | CSS `@keyframes` + Tailwind `animate-*` | Already using CSS animations for pulse effects in globals.css |
| Session creation | Custom session init flow | Existing `POST /sessions` endpoint | Already works, "Begin Anew" just calls it |
| Status derivation | Frontend status calculation | Backend `DeriveStatus()` via SSE | Backend already derives status from domain state |

## Common Pitfalls

### Pitfall 1: Misery Pulse Fires on Every Render
**What goes wrong:** MiseryTracker component re-renders on any Zustand state change, causing the pulse animation to replay.
**Why it happens:** CSS animation restarts when element is re-mounted or class is toggled.
**How to avoid:** Track `previousMiseryCount` in a `useRef`. Only add the `animate-pulse` class when count increases. Remove class after animation completes (use `onAnimationEnd` callback).
**Warning signs:** Misery pips constantly pulsing during gameplay.

### Pitfall 2: End Card Appears Before Narrator Finishes
**What goes wrong:** `state_update` with `status: "ended"` arrives before narrative streaming completes, causing end card to overlay mid-message.
**Why it happens:** In `GameSessionService`, tool results and state_update are written after narrative streaming. But the frontend processes events asynchronously.
**How to avoid:** Show end card only after `isStreaming` becomes `false` AND `status === "ended"`. Use an effect that watches both values.
**Warning signs:** End card blocking the narrator's final words.

### Pitfall 3: sessionStore.setStateUpdate Drops New Fields
**What goes wrong:** Adding new fields to `StateUpdateEvent` but forgetting to map them in `setStateUpdate`.
**Why it happens:** `setStateUpdate` manually maps SSE fields to `CharacterData`. New fields need explicit mapping.
**How to avoid:** Update both the `StateUpdateEvent` type, `CharacterData` type, AND the `setStateUpdate` mapping function. Same for `hydrateCharacter` in `GameSessionPage`.
**Warning signs:** Injury/equipment data not showing despite backend sending it.

### Pitfall 4: DicePlugin Return Type Breaking Existing Behavior
**What goes wrong:** Changing DicePlugin.Roll from `int` to `DiceRollResult` may break the AI agent's ability to use the result in subsequent calls.
**Why it happens:** The GM agent uses dice results in its reasoning. If the return changes from a simple number to an object, the agent may not extract the numeric value correctly.
**How to avoid:** Return a `string` description alongside the numeric result, or keep the return as a string that the Semantic Kernel can interpret (e.g., `"Rolled d20: 14"`). Alternatively, add a `[Description]` attribute to the result type's properties so the LLM understands the structure. Test that the agent still functions correctly after the change.
**Warning signs:** Agent producing garbled narrative about dice results.

### Pitfall 5: Missing miseryCount in Zustand Store
**What goes wrong:** MiseryTracker can't read misery count because `setStateUpdate` doesn't store it.
**Why it happens:** Current `setStateUpdate` only stores characterData and status. `miseryCount` from `StateUpdateEvent` is ignored.
**How to avoid:** Add `miseryCount: number` to `SessionState` and map it in `setStateUpdate`.
**Warning signs:** Misery pips always showing 0.

### Pitfall 6: Read-Only Mode Not Disabling All Inputs
**What goes wrong:** Ended sessions still allow typing or sending messages.
**Why it happens:** ChatInput `disabled` prop is currently only tied to `isStreaming`, not `status`.
**How to avoid:** Pass `status === "ended"` as an additional disabled condition to ChatInput.
**Warning signs:** Player can type in ended sessions.

## Code Examples

### Enriched ToolResultCallout for Dice Rolls
```typescript
// Extended ToolResultCallout rendering
interface DiceRollData {
  formula: string;
  result: number;
}

function isDiceRollData(result: unknown): result is DiceRollData {
  return (
    typeof result === "object" &&
    result !== null &&
    "formula" in result &&
    "result" in result
  );
}

// Inside ToolResultCallout:
if (isDice && isDiceRollData(toolResult.result)) {
  return (
    <div className="border border-doom-yellow/60 bg-doom-dark rounded px-3 py-2">
      <p className="text-doom-yellow text-xs font-bold uppercase tracking-wider mb-1">
        FATE DECIDES
      </p>
      <p className="text-doom-yellow font-bold text-sm">
        {toolResult.result.formula} = {toolResult.result.result}
      </p>
    </div>
  );
}
```

### MiseryTracker Component
```typescript
// 7 pips, filled pips glow pink, empty pips are ash
interface MiseryTrackerProps {
  count: number;
}

export default function MiseryTracker({ count }: MiseryTrackerProps) {
  const prevCountRef = useRef(count);
  const [pulseIndex, setPulseIndex] = useState<number | null>(null);

  useEffect(() => {
    if (count > prevCountRef.current) {
      setPulseIndex(count - 1); // pulse the newly filled pip
      const timer = setTimeout(() => setPulseIndex(null), 1000);
      prevCountRef.current = count;
      return () => clearTimeout(timer);
    }
    prevCountRef.current = count;
  }, [count]);

  return (
    <div className="flex items-center gap-1" aria-label={`Misery tracker: ${count} of 7`}>
      {Array.from({ length: 7 }, (_, i) => (
        <div
          key={i}
          className={`w-2 h-2 rounded-full transition-colors duration-300 ${
            i < count
              ? `bg-doom-pink ${i === pulseIndex ? "animate-[doom-pulse_0.6s_ease-in-out]" : ""}`
              : "bg-doom-ash/40"
          }`}
        />
      ))}
    </div>
  );
}
```

### EndCard Component Structure
```typescript
interface EndCardProps {
  characterName: string;
  isDead: boolean;
  worldEnded: boolean;
  miseryCount: number;
  onRestart: () => void;
}

// Title: isDead ? "YOUR WRETCH HAS FALLEN" : "THE WORLD HAS ENDED"
// Accent: isDead ? "text-doom-pink" : "text-doom-yellow"
// Button: "BEGIN ANEW" -> calls POST /sessions, navigates to new session
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Bare int from DicePlugin | Structured DiceRollResult | This phase | Frontend can render breakdown without parsing |
| StateUpdateEvent without injury data | Full character state in SSE | This phase | Frontend has complete character visibility |
| No end-game UI | End card overlay with restart | This phase | Complete session lifecycle |

## Open Questions

1. **DicePlugin return type and LLM compatibility**
   - What we know: Semantic Kernel serializes return values and passes them to the LLM as function results. Currently returns `int`.
   - What's unclear: Whether changing to a complex return type confuses the LLM agent's ability to use the value in subsequent reasoning (e.g., comparing roll to DR).
   - Recommendation: Return `DiceRollResult` with `[Description]` attributes on properties. If LLM issues arise, fall back to returning a formatted string like `"d20 -> 14"` that the frontend can parse (contradicts D-02 slightly but maintains LLM compatibility). Test manually after change.

2. **"Cause of death" for end card**
   - What we know: Backend has `isDead` and `worldEnded` flags. The end card needs to distinguish death from apocalypse.
   - What's unclear: Whether both flags could be true simultaneously (character dies AND world ends on same turn). Backend sends these as separate booleans.
   - Recommendation: Priority order: if `worldEnded`, show apocalypse card (trumps individual death). Otherwise if `isDead`, show death card. Add both booleans to StateUpdateEvent.

3. **Session stats for end card (days survived, miseries witnessed)**
   - What we know: `currentDay` and `miseryCount` are already in StateUpdateEvent.
   - What's unclear: Whether these represent "total" or just "current" values (e.g., does currentDay reset?).
   - Recommendation: `currentDay` starts at 1 and increments, so `currentDay - 1` = days survived. `miseryCount` = miseries witnessed. Both already available, no extra backend work needed.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (backend), no frontend test framework detected |
| Config file | WrtechedWhispers.Tests.csproj |
| Quick run command | `dotnet test WrtechedWhispers/WrtechedWhispers.sln --filter "FullyQualifiedName~Tests" -v q` |
| Full suite command | `dotnet test WrtechedWhispers/WrtechedWhispers.sln` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| MORK-01 | Session lifecycle end states | integration | Backend: verify DeriveStatus returns "ended" when character dead or world ended | Wave 0 |
| MORK-02 | Misery tracker display | manual | Visual verification of 7-pip component | N/A (frontend) |
| MORK-03 | Dice roll structured result | unit | `dotnet test --filter "DicePlugin"` | Wave 0 |
| CHAR-03 | Injury/status indicators | manual | Visual verification of injury badges in drawer | N/A (frontend) |
| CHAR-04 | Equipment condition | manual | Visual verification of armor tier display | N/A (frontend) |

### Sampling Rate
- **Per task commit:** `dotnet test WrtechedWhispers/WrtechedWhispers.sln -v q`
- **Per wave merge:** Full suite
- **Phase gate:** Full suite green before verification

### Wave 0 Gaps
- [ ] DicePlugin structured return test (verify DiceRollResult serialization)
- [ ] StateUpdateEvent enrichment test (verify injury/equipment fields included)
- [ ] DeriveStatus test for ended campaigns (character dead, world ended scenarios)

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection of all files listed in CONTEXT.md canonical references
- `Character.cs` -- injury booleans, IsDead, Shield, Armor.Tier properties (lines 91-111)
- `CalendarOfNechrubel.cs` -- WorldEnded flag (line 18)
- `CharacterDto.cs` -- full DTO with all injury/equipment/status fields (lines 100-123)
- `GameSessionService.cs` -- SSE state_update construction (lines 205-223)
- `SessionEndpoints.cs` -- SessionDetailDto construction (lines 230-291)
- `DicePlugin.cs` -- current Roll method returning int (line 13)
- `sessionStore.ts` -- setStateUpdate mapping (lines 134-155)
- `ToolResultCallout.tsx` -- current dice rendering (lines 34-54)
- `Header.tsx` -- current header layout (lines 8-52)
- `CharacterDrawer.tsx` -- current drawer sections (lines 89-175)

### Secondary (MEDIUM confidence)
- Semantic Kernel function result serialization behavior (based on training knowledge of SK patterns)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no new dependencies, all existing
- Architecture: HIGH - direct extension of existing patterns, all source code inspected
- Pitfalls: HIGH - based on concrete code analysis of current implementation
- DicePlugin LLM compatibility: MEDIUM - depends on LLM behavior with structured returns

**Research date:** 2026-03-24
**Valid until:** 2026-04-24 (stable, no external dependency changes expected)
