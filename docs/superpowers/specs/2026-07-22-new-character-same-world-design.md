# New Character, Same World (the Meat-Grinder Loop)

**Date:** 2026-07-22
**Status:** Approved for planning

## Problem

Death currently ends the campaign: `SessionContext.DeriveStage()` maps a dead character to
`SessionStage.Ended`, and `TurnCoordinator` hard-refuses further turns. Classic Mörk Borg is a
meat grinder — the intended loop is rolling a new wretch into the same dying world. Permadeath
should end the *character's* story, not the world's: the map, journal, misery clock, and fallen
predecessors all persist.

## Decisions (locked with user)

1. **Explicit UI choice** after death: "Roll a new wretch" vs "Abandon this world". No automatic
   continuation, no in-fiction negotiation.
2. **Gear dies with the corpse.** The new wretch rolls fresh starting equipment. No transfer
   mechanics, no domain-enforced looting. The corpse exists only in narration and the journal.
3. **Journal entry + graveyard block.** Burying auto-writes a journal entry; the journal drawer
   shows a GRAVEYARD section (name, day died). No full memorial with stat snapshots.
4. **Chronicle per wretch.** Each character gets their own chat session. The fallen wretch's
   chronicle is compacted into a seed summary for the new one; the world's hard state needs no
   summary because it is domain state re-injected every turn.

## Architecture

The recovery loop reuses the existing derivation machinery end to end. Death remains derived,
never stored. The only new stored state is the graveyard list and additional chat-session rows.

```
alive ──death──> stage=Ended, status="fallen"  (turns refused; UI shows choice panel)
   ├── POST /sessions/{id}/successor: BuryCharacter + new chat session + epitaph summary
   │       └──> Players empty → stage=CharacterCreation → create wretch → Exploration
   └── POST /sessions/{id}/abandon: Campaign.End() → status="ended", terminal
```

World-ended (7 miseries) and explicit `Campaign.End()` remain terminal. Only character death is
recoverable.

## Domain (Core)

**`Campaign`** gains:

- `public sealed record FallenCharacter(Guid Id, string Name, int DayDied);`
- `[JsonInclude] internal List<FallenCharacter> Fallen` exposed as
  `[JsonIgnore] public IReadOnlyList<FallenCharacter> FallenCharacters`. The `[JsonConstructor]`
  gains `List<FallenCharacter>? fallen = null` — existing blobs deserialize unchanged
  (established optional-parameter pattern).
- `BuryCharacter(Guid characterId, string name)`: throws if the id is not in `Characters`;
  removes it from `Characters`, appends `FallenCharacter(id, name, CurrentDay)`, and records a
  journal entry ("Here fell {name}."). No guard against world-ended here — the endpoint gates
  that; the domain method only enforces membership.

"Abandon" needs no new domain code: it reuses `Campaign.End()`.

## Stage & status derivation

`DeriveStage()` is **unchanged**:

- Pre-burial: the dead character still loads → `Ended`. Turns stay refused; the existing
  no-revival-fabrication guarantee holds.
- Post-burial: `Players` is empty → `SessionContextLoader` sets no `CharacterId` → stage derives
  to `CharacterCreation`. The campaign is still started and active, so once the new wretch is
  created and linked, the next turn derives `Exploration`. No auto-start interference:
  `CampaignService`'s auto-start only fires when the campaign is *not* active.

**Status gains one value: `"fallen"`** — when the stage is `Ended` *because the first player is
dead* while `!Campaign.WorldEnded && !Campaign.IsEnded`. Status remains derived, never stored;
`SessionContext.StatusFor` grows a context-aware form (or equivalent) so status and stage cannot
disagree. `"fallen"` is the signal the UI keys the choice panel on. API `DeriveStatus` in
`SessionEndpoints` mirrors the same rule.

`TurnCoordinator`'s refusal message differentiates: fallen → "The wretch has fallen — roll a new
one or abandon this world."; truly ended → existing message.

## Chronicles: one chat session per wretch

**Today:** one chat session per campaign, fetched everywhere with `sessions.FirstOrDefault()`
(unordered). The schema already supports many (`ChatSessionEntity.CampaignId`).

**New rule:** the *active* chronicle is the **latest chat session by `StartedAt`**.
`IChatHistoryRepository.GetSessionsForCampaign` orders newest-first, so every existing
`sessions.FirstOrDefault()` call site picks the latest without changing shape. `TurnCoordinator`
and `GetSessionDetail` therefore need no structural change. Older chronicles remain in the DB as archived transcripts —
not loaded into context, not shown in the UI (a past-chronicles viewer is deferred).

**Successor flow (`POST /sessions/{id}/successor`):**

1. Ownership check (existing clone pattern: `GetForUser` → `FirstOrDefault` → 404).
2. Validate: first player exists and `IsDead`, `!WorldEnded`, `!IsEnded` — else 400/409.
3. `campaign.BuryCharacter(...)`, save campaign.
4. Create a new chat session (existing `CreateSession`).
5. **Epitaph summary:** one LLM call over the old chronicle (reusing the `ChatHistoryReducer`
   summarize-call shape) with adapted instructions: *the previous wretch is dead — tell their
   tale in past tense, third person; preserve NPCs, locations, unresolved hooks, world events;
   do NOT carry their HP, inventory, or identity as "you".* Store as the new session's
   `ChatSummary(text, coveredCount: 0)` — `ChatHistoryReducer.Compose` then injects it as a
   system message every turn with zero new plumbing.
6. **Graceful degradation:** if summarization fails, bury anyway and seed nothing. Hard state
   (map, journal, POIs, miseries, graveyard) is domain state re-injected every turn via
   `FormatSnapshot`; the summary only carries soft tissue (NPC voices, tone, hooks).

This answers the model-confusion risk directly: the new context contains no messages where
"you" is the dead character — only a past-tense summary plus authoritative domain state.

**Abandon flow (`POST /sessions/{id}/abandon`):** ownership check → `campaign.End()` → save.
Abandoning an already-ended campaign returns 409.

## Engine

- `SessionContextLoader`: unchanged (dead char loads pre-burial; nothing loads post-burial).
- `FormatSnapshot`: gains a Graveyard block when `FallenCharacters` is non-empty
  ("Fallen wretches: {name}, died day {n}") so the narrator can reference predecessors.
- CharacterCreation stage prompt: one added bullet — when the campaign has fallen characters,
  frame creation as a new doomed soul entering the same ongoing world; the predecessor is dead
  and stays dead; their gear is gone.
- Tool catalog/stage maps: no new tools, no stage changes. (Catalog tests unaffected.)

## API surface

| Endpoint | Method | Behavior |
|---|---|---|
| `/sessions/{id}/successor` | POST | Bury + new chronicle + epitaph seed. 404 not-owner, 400/409 if character alive or world/campaign ended. |
| `/sessions/{id}/abandon` | POST | `Campaign.End()`. 404 not-owner. |
| `/sessions/{id}/journal` | GET | Response gains `fallen: [{name, dayDied}]`. |
| `/sessions` (list) | GET | `DeriveStatus` can now return `"fallen"`. |
| `/sessions/{id}` (detail) | GET | Status can be `"fallen"`; transcript pages from the latest chronicle. |

`StateUpdate` (SSE): status field follows the same derivation; no new fields required for v1
(the death panel keys off status `"fallen"` and the character block already carries the name).

## Frontend

- **Play page:** when status is `"fallen"`, render a death panel (doom-styled): "{name} has
  perished" + buttons **Roll a new wretch** → POST successor, then clear transcript state and
  resume chat in CharacterCreation; **Abandon this world** → POST abandon, then show ended
  state. Panel replaces the chat input; no free-text turns while fallen (backend refuses them
  anyway).
- **Journal drawer:** GRAVEYARD block (bone/ash styling) listing fallen wretches with day died,
  from the extended journal response.
- **Session card:** `"fallen"` status chip (distinct style, e.g. pink border, "Fallen" label).
- **Store:** status handling extended; transcript reset on successor.

## Testing

- **Domain:** `BuryCharacter` — moves id to graveyard with current day, writes journal entry,
  throws on unknown id; JSON round-trip with `fallen` present and absent (backward compat).
- **Stage derivation:** dead char → `Ended`; buried (empty players, active campaign) →
  `CharacterCreation`; world-ended stays `Ended` regardless of graveyard.
- **Status:** dead + world alive → `"fallen"`; dead + world ended → `"ended"`; abandoned →
  `"ended"`.
- **Endpoints** (existing `WebApplicationFactory` harness): successor happy path (graveyard
  grows, players empty, new chat session exists, latest-session rule picks it), successor with
  living character → 400/409, ownership → 404, abandon → ended. Epitaph summarization mocked;
  failure path still buries.
- **Repository:** latest-chronicle ordering rule.
- **Evals:** existing dead=ended eval remains valid pre-burial. A successor eval (narrator
  creates a new wretch without resurrecting the fallen one's identity/gear) is a follow-up,
  not part of this change.

## Deliberately skipped

- Corpse looting / gear transfer (decision 2).
- Full memorial with stat snapshots (decision 3).
- Past-chronicles viewer in the UI (old transcripts stay in DB).
- Successor eval for DomainAuthorityEvals (follow-up).
- Multi-character parties — everything continues to assume one active character
  (`Players.First`), now "first living wretch, one at a time".
