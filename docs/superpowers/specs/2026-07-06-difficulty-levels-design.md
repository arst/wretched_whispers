# Difficulty Levels — Design

**Date:** 2026-07-06
**Status:** Approved (pending spec review)
**Branch:** `feat/difficulty-levels` (off `main`)

## Problem

Sessions are created with hard-coded defaults (dawn die `d6`, base MORK BORG
survivability). Players report dying in a couple of turns, which makes the game
frustrating for those who want to experience the world at their own pace, while
others want the raw brutality. There is no way to choose.

## Goal

Let the player pick a **difficulty** when creating a new session. Difficulty is
a single named bundle of already-existing knobs so people can tune their own
chances without us exposing raw dice settings.

Four levels, easy → hard: **Story Mode**, **Grim**, **Doomed**, **Hardcore**.
Default is **Grim** (the balance already tuned on `main`).

## What difficulty controls

| Knob | Mechanism today | Difficulty makes it… |
|---|---|---|
| Starting HP | `max(1, Tough + d8)` in `CharacterCreationService` | `… + StartingHpBonus` |
| Challenge damage | `Character.SufferConsequence` hard-codes Minor d2 / Serious d4 / Deadly d6 | per-level dice |
| Doom pace | `Campaign.DawnDice` (a `1` on the dawn die triggers a Misery; the 7th ends the world) | per-level die — smaller = faster doom |
| GM tone | fixed prose in `StagePrompts` | per-level tone line appended by `PromptComposer` |

Out of scope (explicitly deferred): scaling combat damage, healing, infection.

## The preset table

Base MORK BORG for reference: HP `max(1, Tough+d8)` (~5 avg), Minor d2 / Serious
d6 / Deadly d10, dawn d6.

| Level | HP bonus | Minor | Serious | Deadly | Dawn die | GM tone (appended to prompt) |
|---|---|---|---|---|---|---|
| **Story Mode** | +8 | d2 | d2 | d4 | d8 (crawls) | Forgiving. Favor tension over death; use None/Minor, reserve Deadly for reckless suicide. |
| **Grim** *(default)* | +0 | d2 | d4 | d6 | d6 | Measured. Default None/Minor; Serious for real danger; Deadly only for explicit death-traps. |
| **Doomed** | +0 | d2 | d6 | d10 | d6 | True MORK BORG — unfair and grim; let Serious and Deadly fall as the fiction demands. |
| **Hardcore** | +0 | d4 | d8 | d12 | d4 (races) | Merciless. The world wants them dead; reach readily for Serious and Deadly. |

**Grim == the values currently on `main`** (Serious d4 / Deadly d6, dawn d6, no HP
bonus). So Grim is behavior-preserving; the other three fan out around it.

## Architecture

New in `WretchedWhispers.Core`, namespace `Core.Campaigns` (difficulty is a
campaign-scoped concept):

- `enum Difficulty { StoryMode, Grim, Doomed, Hardcore }`
- `sealed record DifficultySettings(int StartingHpBonus, DiceExpr MinorDamage, DiceExpr SeriousDamage, DiceExpr DeadlyDamage, DiceExpr DawnDice, string GmToneNote)`
- `static class DifficultyPresets { static DifficultySettings For(Difficulty level) => … }` — the pure table above.

**Single source of truth: the `Campaign` aggregate.** Difficulty is chosen at
session creation and stored on the campaign (serialized in the existing
`CampaignEntity.Data` JSON blob → **no EF migration**). It is *not* duplicated
onto the `Character`; the consequence dice are threaded into challenge
resolution at call time instead. Rationale: avoids a redundant second copy and a
cross-aggregate write, and difficulty is a campaign-wide setting.

### Data flow

1. **Create session** — `POST /sessions` gains an optional `difficulty` field
   (defaults `Grim`). `SessionEndpoints.CreateSession` calls
   `Campaign.Create(difficulty, name, description)`, which sets `Difficulty` and
   `DawnDice = DifficultyPresets.For(difficulty).DawnDice`.
2. **Create character** — `CharacterTools.CreateCharacter` reads
   `sessionContext.Campaign?.Difficulty ?? Difficulty.Grim` and passes it to
   `CharacterCreationService.Create(name, difficulty)`, which adds
   `StartingHpBonus` to the rolled HP.
3. **Challenge** — `CharacterTools.ChallengeCharacter` resolves
   `DifficultyPresets.For(campaign.Difficulty)` and passes the settings through
   `CharacterService.ChallengePlayer(…, settings)` to
   `Character.SufferConsequence(consequence, settings, dice)`, which rolls the
   per-level severity die.
4. **Prompt** — `PromptComposer.Compose(context)` appends
   `DifficultyPresets.For(context.Campaign?.Difficulty ?? Grim).GmToneNote`.

### Doom pace moves out of the model's hands

Today the model sets the dawn die via the `ConfigureCampaign` tool
(`diceExpression` param) — see `CampaignTools.ConfigureCampaign`,
`CampaignService.ConfigureCampaign`, `Campaign.Configure`, and the
`StagePrompts` CharacterCreation/CampaignSetup steps. Difficulty now owns the
doom pace, so:

- `ConfigureCampaign` tool drops `diceExpression`; it takes only `name` +
  `description`.
- `CampaignService.ConfigureCampaign` and `Campaign.Configure` drop the
  `dawnDice` parameter. `Configure` preserves the `DawnDice` already set from
  difficulty at `Create`.
- `StagePrompts` CharacterCreation & CampaignSetup steps drop the "choose a
  dawn-roll pace" instruction.

### Prompt severity leaning

The Exploration prompt currently hard-codes a Grim-flavored severity leaning
("default to None or Minor … reserve Deadly for explicit death-traps"), which
would contradict the Hardcore tone. Move the *severity* leaning into the
per-level `GmToneNote`; the base Exploration prompt keeps only the
frequency/integrity rules (challenge only real stakes; never narrate harm
without rolling). `ChallengeCharacter`'s tool description drops the specific
dice sizes (they now vary by level) and keeps the qualitative guidance.

### API surface

- New `CreateSessionRequest(Difficulty Difficulty)`; the endpoint accepts an
  optional body and defaults to `Grim` when absent.
- `Difficulty` added to `SessionPreviewDto` and `SessionDetailDto` (+ their
  mappers) so the UI can display the level.

### Frontend (`wretched-whispers-web`)

- `types/api.ts`: `Difficulty = "StoryMode" | "Grim" | "Doomed" | "Hardcore"`;
  add to the create-session request and to the session DTOs.
- "New Session" no longer fires an immediate POST. It opens a **DifficultyPicker**
  (modal or inline panel, doom aesthetic) listing the four levels with a
  one-line flavor description each; `Grim` pre-selected. Confirm → `POST /sessions`
  with `{ difficulty }` → navigate to the session.
- `SessionCard` shows a small difficulty label.

## Backward compatibility

Existing persisted campaigns lack `Difficulty` in their JSON. The
`[JsonConstructor]` gives the parameter a default of `Difficulty.Grim`, so old
campaigns load as Grim; their already-stored `DawnDice` (d6) is preserved.

## Testing

- `DifficultyPresetsTests` — `For(level)` returns the expected settings (theory over all four).
- `ChallengeConsequenceTests` — updated to the new `SufferConsequence(consequence, settings, dice)` signature; verify the per-level severity die is rolled.
- `CharacterCreationService` — HP bonus applied (Story +8, Grim +0).
- `Campaign` — `Create(difficulty, …)` sets `Difficulty` + preset `DawnDice`; `Configure` preserves `DawnDice`; deserializing a blob without `Difficulty` yields Grim.
- `PromptComposer` — appends the tone note for a given difficulty.
- `CampaignTools.ConfigureCampaign` — no longer accepts a dice expression (signature/desc).
- Endpoint (optional integration) — `POST /sessions` with a difficulty persists it and surfaces it on the session DTO.

## Design decisions / alternatives considered

- **Store on Campaign only** (chosen) vs. also baking onto `Character`. Rejected
  duplication; thread settings into challenge resolution instead. Trade-off:
  challenge resolution needs the campaign in `SessionContext` (it always is
  during Exploration); fall back to `Grim` if absent (no null-forgiving).
- **Difficulty owns the dawn die** vs. leaving it model-controlled. Chosen for
  determinism and consistency with "domain owns truth, model narrates."
- **Locked at creation** vs. changeable mid-campaign. Locked — HP and dawn die
  are creation-time; no mid-campaign switch in v1.
- **Reconciliation with the challenge-balance work:** already merged to `main`;
  Grim preset encodes those exact values, so no separate step needed.
