# Getting Better — Mörk Borg post-adventure progression

**Date:** 2026-07-23
**Status:** Approved design

## Context

Mörk Borg's codified leveling ritual ("Getting Better", core rules): whenever the GM decides —
usually after a completed scenario — roll 6d10; if the total is **≥ current max HP**, max HP
increases by d6. Then roll a d6 against each of the four abilities: **d6 ≥ score → +1** (cap +6),
**d6 < score → −1** (floor −3) — improvement *or worse*.

The engine has `ImproveCharacterAbility`/`DegradeCharacterAbility` (Resolution-stage,
GM-judgment, for story-driven blessings/curses) but no codified ritual. Per the house
anti-fabrication pattern, the whole ritual must resolve atomically in one tool call: the domain
rolls everything, the model narrates the returned outcome (same shape as `Challenge` /
`ResolveCombatRound`).

## Decisions (user-approved)

1. **Trigger:** GM-judgment tool + domain rest gate. The model calls the tool when the fiction
   concludes a genuine adventure; the domain refuses unless the character has taken a full
   night's rest since the last Getting Better. Prompt guards judgment; domain guards spam.
2. **"Or worse" is difficulty-dependent:** StoryMode is improvement-only; Grim/Doomed/Hardcore
   keep RAW decreases. One bool in `DifficultySettings`.
3. **HP increase raises Max only** — current HP is unchanged (RAW-faithful; rest heals).
4. **Rest gate initializes false:** a new character must sleep one full night before the first
   Getting Better. Flag is set by a full night's rest, consumed by the roll.

## Domain (Core)

### `DifficultySettings`
Append `bool AbilityLossOnGettingBetter`. Presets: StoryMode `false`; Grim, Doomed,
Hardcore `true`.

### `HitPoints`
Add `IncreaseMax(int amount)` → returns `this with { Max = Max + amount }`. Current unchanged.

### `Character`
- New persisted state: `bool CanGetBetter` (init `false` in `Create`; round-trips via
  JsonConstructor like other state).
- `Rest(...)`: on a full night's rest (existing 8+ hour threshold), set `CanGetBetter = true`.
  The infected early-return does NOT set it (no ritual progress while infection blocks healing).
- `GetBetter(Dice dice, bool allowAbilityLoss)` → `GettingBetterOutcome`:
  1. Throw `InvalidOperationException` if `!CanGetBetter` (message: requires a full night's
     rest since the last Getting Better).
  2. HP: roll 6d10 (sum). If total ≥ `Hp.Max`, roll d6 and `Hp = Hp.IncreaseMax(d6)`.
  3. For each ability in a fixed order (Strength, Agility, Presence, Toughness): roll d6.
     - d6 ≥ modifier and modifier < +6 → `Improve(kind, 1)`
     - d6 < modifier and `allowAbilityLoss` and modifier > −3 → `Degrade(kind, -1)`
     - otherwise unchanged.
     Reusing `Improve`/`Degrade` keeps the Strength → inventory-capacity recalc.
  4. Set `CanGetBetter = false`.
  5. Return the outcome.

### `GettingBetterOutcome` (Core value object)
```
sealed record AbilityChange(AbilityKind Kind, int Roll, int Delta, int NewScore);
sealed record GettingBetterOutcome(
    int HpRoll,          // the 6d10 total
    int HpGained,        // 0 when the check failed
    int NewMaxHp,
    IReadOnlyList<AbilityChange> Abilities);
```

### `CharacterService`
`GetBetter(Guid characterId, bool allowAbilityLoss)`: load → `character.GetBetter(dice,
allowAbilityLoss)` → save → return outcome. The difficulty flag is resolved by the caller
(the Engine holds the campaign).

## Engine

### `GettingBetterOutcomeDto`
Mirrors `GettingBetterOutcome` (per-ability roll + delta + new score, HP roll, HP gained, new
max) so the model narrates real dice. Doc comment: negative deltas are the RAW "or worse" —
narrate the regression, don't soften it into nothing.

### `CharacterTools.GettingBetter()`
`[GameTool(SessionStage.Exploration, SessionStage.Resolution)]`. Resolves
`AbilityLossOnGettingBetter` from `sessionContext.Campaign?.Difficulty ?? Difficulty.Grim`
via `DifficultyPresets.For(...)` (existing idiom at `CharacterTools.cs:41`). Description:
call ONLY when the fiction concludes a genuine adventure or scenario — a quest completed, a
dungeon survived, a nemesis dead — never after a routine fight; requires a full night's rest
since the last Getting Better (the domain refuses otherwise); the domain rolls everything
(HP check and all four abilities) — narrate the returned result.

### `NarratorPersona`
One bullet: Getting Better is the codified post-adventure ritual — offer it (via its tool)
when an adventure truly concludes and the character has rested; `ImproveCharacterAbility` /
`DegradeCharacterAbility` are for story-driven blessings and curses only, never for leveling.

## Tests

Existing idioms: `TestBase`, 0-based dice mocks (`SetupDiceRoll(sides, result)` → die value
result+1; `SetupDiceRolls` sequential queue), `TestCharacters.Create`.

- **HP gate:** 6d10 ≥ max → max +d6, current unchanged; 6d10 < max → max unchanged.
- **Ability up:** d6 ≥ score → +1; at +6 → unchanged.
- **Ability down:** d6 < score with `allowAbilityLoss: true` → −1; at −3 → unchanged;
  with `allowAbilityLoss: false` (StoryMode) → unchanged.
- **Strength change** recalculates inventory capacity.
- **Rest gate:** fresh character → `GetBetter` throws; full night's rest sets the flag; partial
  rest does not; infected full rest does not; a resolved `GetBetter` clears it (second call
  throws).
- **Service:** saves once; outcome returned.
- **DTO:** maps outcome faithfully.
- **Presets:** `AbilityLossOnGettingBetter` false only for StoryMode.

## Verification

1. `dotnet build WrtechedWhispers/WrtechedWhispers.sln`
2. `dotnet test WrtechedWhispers/WrtechedWhispers.sln` (narrator prompt changed — watch
   DomainAuthorityEvals if creds present)
3. Manual playtest: conclude an adventure, rest a full night, ask the GM about getting better —
   ritual resolves in one tool call, drawer shows new max HP/abilities; calling again without a
   rest is refused in-world.

## Deliberately skipped

- No UI work (drawer already renders HP/abilities from CharacterDto).
- No new eval (small prompt delta; add if playtests show tool spam).
- No silver/equipment extras — RAW Getting Better is HP + abilities only.
- Reactive uses, multi-character parties: out of scope.
