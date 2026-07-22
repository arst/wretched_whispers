# Domain-Enforced Reaction Rolls

**Date:** 2026-07-22
**Status:** Approved for planning

## Problem

The Mörk Borg reaction table already exists in the domain (`Encounter.RollInitialReaction`,
proper 2d6 spread: 2–3 Kill, 4–6 Angered, 7–8 Indifferent, 9–10 AlmostFriendly, 11–12 Helpful)
but is a dead end:

1. It only fires when the model creates an encounter as `Unknown`, and no prompt ever tells it
   to — the Exploration prompt says create encounters "when violence or combat begins", so
   encounters arrive pre-declared `Hostile` and the table never rolls. Hostility is decided by
   model judgment — the fabrication shape this project keeps closing.
2. The rolled result is discarded: collapsed to a binary `CurrentType` that nothing consumes
   (not `EncounterDto`, not `FormatSnapshot`, not stage derivation). Even a model that passes
   `Unknown` gets no disposition back and must invent one.
3. The five-step nuance (Kill vs Angered, Indifferent vs Helpful) is lost at roll time.

**Latent bug (load-bearing):** for pre-declared encounters `Initiate` returns early, so
`CurrentType` stays `default(EncounterType)` = `Friendly` — even for explicitly Hostile
encounters. Unnoticed because nothing reads `CurrentType`. The new guard (below) would break
every ambush without fixing this first.

## Decisions (locked with user)

1. **Encounter-scoped.** Reaction rolls live on encounter creation only. Pure social NPCs stay
   narrative. No standalone reaction tool.
2. **Prompt-guided default.** All three creation types stay; prompts make `Unknown` the default
   for any first meeting. `Hostile`/`Friendly` pre-declares are reserved for
   fiction-predetermined attitude (ambush, sworn enemy, hired guide).
3. **Domain-enforced.** `StartEncounter` throws while the encounter is Friendly. A new
   `TurnHostile` transition (domain method + tool) flips it when the fiction legitimately
   escalates. The roll is real state the model cannot silently override.
4. **Approach A** — wire the existing machinery; no per-step mechanical effects.

## Domain (Core)

**`Encounter`** changes:

- New stored state: `[JsonInclude] public InitialReaction? Reaction { get; private set; }` and
  `[JsonInclude] public int? ReactionRoll { get; private set; }` (the raw 2d6 total, so the
  narrator can show real dice). Both are optional `[JsonConstructor]` params defaulting `null`
  — old blobs and pre-declared encounters deserialize unchanged (established
  optional-parameter pattern; note `AggregateJsonOptions` camelCase: serialized names are
  `reaction`, `reactionRoll`).
- `Initiate`:
  - Non-`Unknown` path: set `CurrentType = initialType` (the latent-bug fix), leave
    `Reaction`/`ReactionRoll` null.
  - `Unknown` path: roll once, store both `Reaction` and `ReactionRoll`, then collapse as
    today (Kill/Angered → Hostile, else Friendly).
- `StartEncounter()`: throws `InvalidOperationException` while `CurrentType == Friendly`
  ("The encounter is friendly — call TurnEncounterHostile first; the fiction must escalate before
  combat can start."). Existing adversary-count guard unchanged.
- New `public void TurnHostile()`: throws `InvalidOperationException` if `IsEnded`; otherwise
  sets `CurrentType = Hostile`. **Idempotent** — a no-op when already Hostile or already
  started, so a confused model always has a safe landing (no retry dead-end).

`RollInitialReaction` keeps its private table; only its call site changes to capture the roll.

## Service (Core)

`EncounterService.TurnHostile(Guid encounterId)`: get (throw "Encounter not found" if missing,
existing pattern), `encounter.TurnHostile()`, save. Nothing else in the service changes.

## Engine

- **`EncounterDto`** gains:
  - `Disposition` (string: `"Friendly"`/`"Hostile"`, from `CurrentType`),
  - `Reaction` (string?: `"Kill"`/`"Angered"`/`"Indifferent"`/`"AlmostFriendly"`/`"Helpful"`,
    null when pre-declared),
  - `ReactionRoll` (int?, null when pre-declared).
- **New tool** `TurnEncounterHostile` on `EncounterTools`, `[GameTool(SessionStage.Exploration)]`,
  no parameters (uses `RequireEncounterId()`). Description: only when the fiction legitimately
  escalates — the player attacks first, negotiation collapses, treachery is revealed; never to
  override a rolled reaction without in-fiction cause. Returns the updated `EncounterDto`.
- **`CreateEncounter`** `initialEncounterType` description rewritten: `Unknown` = the domain
  rolls the Mörk Borg reaction table and returns the result — the DEFAULT for any first
  meeting; pre-declare `Hostile`/`Friendly` only when the fiction predetermines the attitude
  (ambush, sworn enemy, hired guide).
- **`FormatSnapshot`** active-encounter block gains a disposition line, with reaction when
  present (e.g. `  Disposition: Hostile (reaction roll 4 — Angered)` / `  Disposition:
  Friendly`).

## Prompts

`StagePrompts` Exploration — the single "when violence begins" bullet becomes guidance
covering:

- First meeting with uncertain attitude → `CreateEncounter` with `Unknown`; narrate the rolled
  reaction honestly (the roll and its result come back in the tool response).
- Pre-declare `Hostile`/`Friendly` only when the fiction predetermines it.
- `StartEncounter` only once the encounter is hostile; if a friendly meeting collapses into
  violence, call `TurnEncounterHostile` first.

`NarratorPersona` untouched.

## Lifecycle audit (why nothing gets stuck)

- **Friendly, never started** — the new state this feature creates. Cannot enter Combat
  (guard) and cannot be `EndEncounter`ed (requires `IsStarted`), but needs no closing:
  `DeriveStage` requires `IsStarted` for Combat, and `SessionContextLoader` filters on
  `IsStarted`, so the encounter is simply not reloaded next turn. It fades. `CompleteResolution`
  is only reachable from Resolution ⇒ `IsEnded` ⇒ `IsStarted` — an unstarted encounter blocks
  nothing.
- **Cross-turn escalation of a faded friendly meeting** — `TurnEncounterHostile` throws a clean
  "no active encounter" error; the model creates a new encounter legitimately pre-declared
  Hostile (the player attacking IS fiction-predetermined). Accepted, not a gap.
- **Legacy blobs** — pre-deploy encounters persist `CurrentType = Friendly` even when declared
  Hostile (the latent bug). Unstarted ones are never reloaded; already-started ones never pass
  through `StartEncounter` again (`ResolveRound` doesn't check `CurrentType`). Worst case is a
  legacy mid-combat session showing `Disposition: Friendly` in the snapshot; combat itself
  proceeds unaffected (`ResolveRound` never reads `CurrentType`). No migration.
- **Residual (named, not fixed):** `SessionContextLoader` does one repository `Get` per
  attached encounter id until it finds a started-unresolved one; faded encounters lengthen
  that scan marginally. Pre-existing cost class.

## Testing

- **Domain:** `Unknown` creation stores `Reaction` + `ReactionRoll` and maps each band
  correctly (mocked dice across all five bands); pre-declared Hostile → `CurrentType` Hostile
  (latent-bug regression); pre-declared Friendly → `CurrentType` Friendly, null reaction;
  `StartEncounter` on Friendly throws; `TurnHostile` then `StartEncounter` (with adversary)
  succeeds; `TurnHostile` on ended encounter throws; `TurnHostile` idempotent when already
  Hostile/started.
- **Persistence:** JSON round-trip with reaction fields present and absent (legacy blob with
  no `reaction`/`reactionRoll` keys deserializes to nulls).
- **Engine:** `EncounterDto` carries disposition/reaction/roll; `FormatSnapshot` disposition
  line; catalog/tool-provider tests gain `TurnEncounterHostile` in Exploration expectations.
- **Evals:** a reaction-honoring eval for `DomainAuthorityEvals` (model uses `Unknown` on an
  uncertain meeting and narrates the rolled result without overriding it) is a follow-up, not
  part of this change.

## Deliberately skipped

- Per-step mechanical effects (Kill = surprise round, Helpful = concrete boon) — revisit after
  playtests.
- Reaction rolls for non-encounter social NPCs (decision 1).
- Cross-turn persistence of unstarted friendly encounters (fading is the design).
- Reaction-honoring eval (follow-up).
- Cleanup/compaction of faded encounter ids on the campaign.
