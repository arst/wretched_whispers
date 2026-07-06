# Domain Authority & GM Flexibility — Design

**Date:** 2026-07-05
**Status:** Approved
**Principle:** the model chooses intent, the domain owns truth. This design pushes that principle
into the last places where prompt-English still does a machine's job, and opens domain-validated
channels for the GM judgment Mork Borg actually needs.

## Background

Architecture review (2026-07-05) found the guardrail stack (stage-scoped tool registration, derived
stages, ID hiding, tool-error feedback, fabrication guard, transactional turns, evals) solid, with
seven improvements. All seven are in scope, grouped into five phases. Each phase is a branch + PR.

## Item 1 — Rolling summary persistence

**Problem:** `ChatHistoryReducer` re-summarizes the entire message prefix on every turn once a
session exceeds 150 messages. Unbounded, repeated cost.

**Design:** persist a rolling summary with a watermark.

- New storage on the chat-history side: per chat session, `(CoveredThroughCount, SummaryText)`.
  Exposed via `IChatHistoryRepository` (e.g. `GetSummary` / `SaveSummary`) — no new repository
  abstraction.
- `ReduceAsync` gains the `chatSessionId` parameter. Logic:
  1. Load stored summary. The *unsummarized tail* is `history.Skip(CoveredThroughCount)`.
  2. Tail ≤ threshold (150): return `[summary system message, ...tail]` (or just history when no
     summary exists yet). **No model call.**
  3. Tail > threshold: summarize `[current summary] + tail.Take(tail.Count - Target)` into an
     updated summary (bounded window — never the whole prefix), persist it with the advanced
     watermark, return `[new summary message, ...recent Target messages]`.
- Message counts are stable because chat history is append-only.
- Existing empty-summary fallback behavior is preserved (keep tail, don't advance watermark).

**Model-visible behavior unchanged** (same `[Summary of the session so far]` system message).

## Item 2 — Domain-resolved combat round

**Problem:** the Combat prompt is an algorithm in English ("call AttackPlayer once per living
adversary… call EndEncounter if all dead"). Prompt law, not code law — the historical source of
runaway/fabricated combat.

**Design:** one round = one domain operation.

New in Core:

```
enum PlayerRoundAction { Attack, Flee, Other }

EncounterService.ResolveRound(encounterId, characterId, PlayerRoundAction action, string? targetName)
    -> CombatRoundOutcome
```

Resolution order inside `ResolveRound`:

1. **Player action.**
   - `Attack`: existing attack pipeline against the named adversary (case-insensitive; falls back
     to first living when the name doesn't match, as today).
   - `Flee`: **new domain rule** — Agility test vs DR 12. Success ends the encounter (player fled);
     failure wastes the round.
   - `Other`: no player mechanics here — the player's action was already resolved by another tool
     this turn (scroll, item). This variant exists so retaliation/bookkeeping still runs exactly
     once. It is the GM-flexibility escape hatch.
2. **Retaliation:** every adversary still living and unfled after step 1 attacks the player once
   (existing `Defend` pipeline). Skipped entirely when step 1 already ended the encounter.
3. **Morale/flee checks:** existing `Encounter` logic runs as part of the round.
4. **Auto-end:** if no living, unfled adversaries remain — or the player fled or died —
   `EndEncounter()` is called inside the same operation.

`CombatRoundOutcome` (returned whole to the model as the tool result):

- Player action outcome with **full dice breakdown** (hit, base roll, crit/fumble, damage, armor
  reduction, weapon broken — the fields `AttackOutcome` already carries; flee: roll vs DR).
- Per-adversary retaliation outcomes (adversary name, damage dealt, full defence breakdown).
- Morale events (who fled).
- `EncounterEnded` + reason (`AllDefeated | PlayerFled | PlayerDead`).

Tool changes in `EncounterTools`:

- Add `ResolveCombatRound(action, targetName?)` — `[GameTool(Combat)]`.
- Remove `[GameTool]` from `AttackPlayer`, `AttackAdversary`, `EndEncounter` (methods and service
  operations may be deleted where nothing else uses them).
- `CharacterTools.CastScroll` gains `SessionStage.Combat` (fixes latent bug: the Combat prompt
  references scroll casting but the tool isn't registered for the stage).

Combat stage prompt shrinks to tone + judgment: distinguish questions from actions; pick the
action variant; narrate the returned packet with its real numbers. No sequencing instructions.

## Item 3 — Campaign lifecycle auto-start

**Problem:** the CharacterCreation/CampaignSetup prompts script `ConfigureCampaign` →
`StartCampaign` verbatim. A scripted sequence is the system's job.

**Design:**

- `Campaign.IsConfigured` flag, set by `Configure(...)`, persisted.
- Auto-start rule in `CampaignService`: after `JoinCampaign` or campaign configuration, if the
  campaign has a player **and** `IsConfigured` **and** not started — `Start()` it. Order-independent.
- `StartCampaign` removed as a model tool (domain `Start()` remains). `ConfigureCampaign` is the
  only remaining model call — the one that genuinely needs model creativity (name, description,
  dawn pace).
- CharacterCreation/CampaignSetup prompts drop the sequencing steps accordingly.
- The existing guard comment in `CharacterTools.CreateCharacter` ("must not silently advance the
  stage machine") is superseded by this design: advancing on *completed setup* is now a
  deterministic domain rule, not a model decision.

## Item 4 — Rulings channel + full dice breakdowns

**Problem:** `ChallengeCharacter` returns a bare bool; no legal consequence path exists outside
combat (a failed climb cannot mechanically hurt the character), and the narrator gets no numbers
to weave despite the prompt demanding it.

**Design:**

- `enum ChallengeConsequence { None, Minor, Serious, Deadly }` in Core.
- `CharacterService.ChallengePlayer(characterId, dr, ability, consequence)`: on failure with a
  consequence, roll domain-defined damage and apply it atomically —
  **Minor = d2, Serious = d6, Deadly = d10.** Damage can kill (Mork Borg-brutal); death flows
  through the existing `IsDead` → `Ended` stage derivation.
- `ChallengeOutcomeDto` returns the full breakdown: `Roll, Modifier, Total, Dr, IsSuccess,
  DamageTaken, IsDead`.
- Tool description guides the model: pick the severity a GM would ("a fall from the rotting
  rampart: Serious").

The model exercises real GM judgment (how risky was that?); the domain owns every number.

## Item 5 — Campaign journal

**Problem:** mechanical state is safe in the DB, but fictional state (NPCs, promises, locations)
lives only in chat history and dies when the summarizer drops it.

**Design:**

- `JournalEntry(JournalCategory Category, string Text, int Day, int Hour)` in Core.Campaigns;
  `enum JournalCategory { Npc, Location, Promise, Quest, Event }`.
- `Campaign.Journal` (append-only list) + `Campaign.RecordJournalEntry(category, text)` stamping
  the campaign's current day/hour. Persisted alongside the aggregate like Miseries/Encounters.
- New tool `CampaignTools.RecordJournalEntry(category, text)` —
  `[GameTool(Exploration, Combat, Resolution)]`. Guard: non-empty text.
- `SessionContext.FormatSnapshot` gains a `## Campaign Journal` section listing all entries
  (`[Day 3, Npc] Grimlod the flagellant — owes the character a lantern`).
- One persona line: record fiction-worthy facts (NPCs met, promises made, places discovered,
  notable events) via `RecordJournalEntry` when they occur.
- No update/delete surface. State changes are new `Event` entries ("Grimlod died at the shrine").

ponytail: full injection of all entries, no cap/retrieval — Mork Borg campaigns are short (the
world ends in days). Add a tail-cap or relevance retrieval only if journals outgrow the context
budget.

## Item 6 — Function-loop iteration cap

`AgentExecutor`'s function-invocation configuration gains a maximum-iterations setting
(`MaximumIterationsPerRequest` ≈ 15, alongside the existing consecutive-error cap of 3). Bounds
runaway productive loops (roll, roll, roll…). One line.

## Item 7 — Evals

Three scenarios on the existing `EvalHost` harness (real-model + cache modes):

1. **Combat round:** seed an active encounter; player message "I attack X" → exactly one
   `ResolveCombatRound` call; a follow-up rules question → zero tool calls. Uses the existing
   `ToolCallOrderEvaluator`.
2. **Journal recording:** exploration turn whose narration introduces an NPC / a promise → expect
   a `RecordJournalEntry` call with a plausible category.
3. **Groundedness judge:** new LLM-judge evaluator comparing the turn's narration against the
   turn's tool-result numbers (damage dealt, rolls) — flags invented or contradicted numbers.
   Lives in the eval project only; **no runtime narration validator** (deliberate: items 2/4
   remove most fabrication fuel; the judge catches regressions for free).

## Phases

| Phase | Items | Notes |
|---|---|---|
| 1 | 1, 6 | Independent quick wins; no behavior change visible to the model |
| 2 | 4 | Rulings + breakdowns; Core + tools + DTOs |
| 3 | 2, 3 | The big one: round resolver, lifecycle auto-start, prompt rewrites |
| 4 | 5 | Journal: Core + tool + snapshot injection |
| 5 | 7 | Evals over the new tool surface; lands last |

Phases 1, 2, 4 are mutually independent. 3 depends on nothing but touches the most. 5 depends
on 3 and 4.

## Testing

- Core unit tests: `ResolveRound` (attack/flee/other, retaliation set, auto-end reasons),
  flee DR rule, challenge consequences (severity dice, death), campaign auto-start
  (both orders), journal append + stamping. Existing `TestBase`/`Dice` seams apply.
- Reducer tests: watermark advance, no-call-under-threshold, empty-summary fallback (fake
  `IChatClient`).
- Tool-layer tests where they exist today; prompts/persona changes covered by phase-5 evals.

## Error handling

- `ResolveCombatRound` with an unknown target name falls back to the first living adversary
  (current `AttackAdversary` behavior) — never a dead turn over a typo.
- All new tool guards throw model-readable messages (existing `ToolGuard` pattern).
- Summary persistence failure falls back to current behavior (summarize in memory, don't advance
  watermark) rather than failing the turn.

## Out of scope

- Runtime narration validation (eval-side judge only).
- Journal retrieval/relevance ranking, entry updates.
- NPC/social mechanics beyond journal entries.
- Combat actions beyond Attack/Flee/Other routed through the round resolver.
