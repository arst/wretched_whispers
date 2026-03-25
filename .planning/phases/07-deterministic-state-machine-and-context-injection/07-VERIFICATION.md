---
phase: 07-deterministic-state-machine-and-context-injection
verified: 2026-03-25T08:00:00Z
status: gaps_found
score: 16/17 must-haves verified
gaps:
  - truth: "Stage transitions fire on correct plugin call completions"
    status: partial
    reason: "CompleteResolution clears the in-memory SessionContext but does NOT call encounter.Resolve() on the domain entity. BuildSessionContextAsync reloads ended-but-unresolved encounters on every turn (condition: enc.IsStarted && !enc.IsResolved). On the turn after CompleteResolution, the unresolved encounter is reloaded and DeriveStage returns Resolution again, trapping the session permanently in Resolution stage."
    artifacts:
      - path: "WrtechedWhispers/WretchedWhispers.Api/Plugins/GameMasterPlugins/ResolutionWrapperPlugin.cs"
        issue: "CompleteResolution() calls sessionContext.ClearActiveEncounter() but never calls encounter.Resolve() to persist IsResolved=true to the database"
      - path: "WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs"
        issue: "BuildSessionContextAsync loads encounters matching enc.IsStarted && !enc.IsResolved (line 365), which includes ended-but-unresolved encounters, causing the Resolution stage to recur"
    missing:
      - "ResolutionWrapperPlugin.CompleteResolution() must load the encounter from IEncountersRepository, call encounter.Resolve(), and save it back before clearing the context"
      - "Inject IEncountersRepository into ResolutionWrapperPlugin constructor"
      - "Add test: CompleteResolution persists IsResolved=true on the domain encounter entity"
---

# Phase 07: Deterministic State Machine and Context Injection Verification Report

**Phase Goal:** Session stages advance deterministically through plugin tool call side effects, with a session context object injected into model prompts so the AI never manages IDs, state, or transitions directly
**Verified:** 2026-03-25T08:00:00Z
**Status:** GAPS FOUND
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Session stage is derived deterministically from domain state (6 stages) | VERIFIED | `SessionContext.DeriveStage()` reads Campaign/Character/Encounter objects and returns one of 6 `SessionStage` enum values with no model involvement |
| 2 | Stage transitions fire on correct plugin call completions | PARTIAL | `StageTransitions` map is correct (5 entries). However `CompleteResolution` does not persist `IsResolved=true` to the domain, so the Resolution→Exploration transition breaks at the persistence boundary (see gaps) |
| 3 | SessionContext tracks character, campaign, and encounter IDs accumulated during a session | VERIFIED | `SessionContext` has `CharacterId`, `CampaignId`, `ActiveEncounterId` with `Set*` mutators; IDs are set by wrapper plugins on creation calls |
| 4 | Resolution stage is distinguishable from exploration via IsResolved flag on Encounter | VERIFIED | `Encounter.IsResolved` property exists; `DeriveStage()` checks `IsEnded && !IsResolved` for Resolution; `Resolve()` method sets flag correctly — but `CompleteResolution` never calls it |
| 5 | Model never sees GUID parameters — wrapper plugins auto-fill IDs from SessionContext | VERIFIED | All 5 wrapper plugins auto-fill IDs; `CharacterWrapperPlugin` has no characterId params; `EncounterWrapperPlugin.AttackPlayer` takes only `attackingAdversaryId`; `StartCampaign()` takes zero params |
| 6 | Guardrail errors return corrective messages that steer the model back | VERIFIED | Guardrails throw `InvalidOperationException` with messages like "No character exists yet -- call CreateCharacter first" and "A character already exists for this session. You cannot create another one." |
| 7 | StagePluginRegistry returns exactly the right functions for each stage | VERIFIED | CharacterCreation=1, CampaignSetup=2, Exploration=10, Combat=4, Resolution=7, Ended=0 functions per stage |
| 8 | System prompt is composed from narrator persona + stage instructions + context snapshot | VERIFIED | `PromptComposer.Compose()` concatenates `NarratorPersona.Text + StagePrompts.For(stage) + context.FormatSnapshot()` |
| 9 | Each stage has focused instructions — no monolithic 14-step prompt | VERIFIED | `StagePrompts` has 6 private const strings, one per stage, replacing the previous monolithic instructions |
| 10 | GameSessionService loads SessionContext at turn start and derives stage | VERIFIED | `BuildSessionContextAsync()` loads campaign/character/encounter from repos; `DeriveStage()` called on result to route execution |
| 11 | Only stage-appropriate functions are advertised to model via FunctionChoiceBehavior.Auto(functions:) | VERIFIED | `GameSessionService.CreateGameMasterAgent` calls `stagePluginRegistry.GetFunctionsForStage(stage, kernel)` then passes to `FunctionChoiceBehavior.Auto(functions: allowedFunctions)` |
| 12 | Wrapper plugins are imported instead of original plugins | VERIFIED | `BuildKernelForSession` imports CharacterWrapperPlugin, CampaignWrapperPlugin, EncounterWrapperPlugin, DiceWrapperPlugin, ResolutionWrapperPlugin via `ImportPluginFromObject` |
| 13 | StageTransitionFilter is registered on the kernel | VERIFIED | `kernel.AutoFunctionInvocationFilters.Add(new StageTransitionFilter(sessionContext))` at line 410 of GameSessionService.cs |
| 14 | Combat is resolved by a separate sub-agent with only combat tools | VERIFIED | `CombatAgentService.ResolveCombat` builds a `ChatCompletionAgent` with `FunctionChoiceBehavior.Auto(functions: combatFunctions)` restricted to Combat-stage functions |
| 15 | Combat sub-agent returns narrative only — domain state mutated by plugin calls during combat | VERIFIED | `CombatAgentService` collects streamed narrative text; encounter/character mutations happen via `EncounterWrapperPlugin.AttackPlayer/AttackAdversary/EndEncounter` plugin calls |
| 16 | Game master receives combat narrative in chat history and continues with updated state | VERIFIED | After `combatService.ResolveCombat(...)`, narrative saved as `AuthorRole.Assistant` message in `chatHistoryRepository`; post-combat context re-loaded via `BuildSessionContextAsync` |
| 17 | Combat sub-agent runs within same DI scope and transaction as game master | VERIFIED | `CombatAgentService` receives the same `gameKernel` (same DI scope); instantiated inline in `ExecuteAgentTurnAsync` within the same transaction |

**Score:** 16/17 truths verified (truth 2 is PARTIAL due to Resolution→Exploration transition gap)

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `WretchedWhispers.Api/Services/SessionStage.cs` | SessionStage enum with 6 values | VERIFIED | Exists; `enum SessionStage` with CharacterCreation, CampaignSetup, Exploration, Combat, Resolution, Ended |
| `WretchedWhispers.Api/Services/SessionContext.cs` | Mutable session context with stage derivation and ID tracking | VERIFIED | Exists; `sealed class SessionContext` with `DeriveStage()`, `SetCharacterId/SetCampaignId/SetActiveEncounterId()`, `FormatSnapshot()`, domain object properties |
| `WretchedWhispers.Api/Services/StageTransitions.cs` | Static transition map (stage, plugin, function) -> next stage | VERIFIED | Exists; `static class StageTransitions` with 5-entry dictionary and `GetNextStage()` |
| `WretchedWhispers.Api/Services/StageTransitionFilter.cs` | IAutoFunctionInvocationFilter that auto-advances stage on plugin success | VERIFIED | Exists; `sealed class StageTransitionFilter` implementing `IAutoFunctionInvocationFilter`; calls `next()` first |
| `WretchedWhispers.Core/Encounters/Encounter.cs` | IsResolved boolean for resolution stage detection | VERIFIED | `[JsonInclude] public bool IsResolved { get; private set; }` and `public void Resolve()` method present |
| `WretchedWhispers.Api/Plugins/GameMasterPlugins/CharacterWrapperPlugin.cs` | Character wrapper hiding characterId, with guardrails | VERIFIED | Exists; auto-fills characterId; guardrails for duplicate creation; auto-joins character to campaign on create |
| `WretchedWhispers.Api/Plugins/GameMasterPlugins/CampaignWrapperPlugin.cs` | Campaign wrapper hiding campaignId, with guardrails | VERIFIED | Exists; `ConfigureCampaign` replaces `CreateCampaign`; `StartCampaign()` zero params; `AdvanceTime/Rest` auto-fill campaignId |
| `WretchedWhispers.Api/Plugins/GameMasterPlugins/EncounterWrapperPlugin.cs` | Encounter wrapper hiding encounterId/adversaryId/characterId | VERIFIED | Exists; `AttackPlayer(Guid attackingAdversaryId)` auto-fills encounterId and playerBeingAttackedId |
| `WretchedWhispers.Api/Plugins/GameMasterPlugins/ResolutionWrapperPlugin.cs` | CompleteResolution function for resolution stage exit | STUB | Exists but incomplete: `CompleteResolution()` calls `sessionContext.ClearActiveEncounter()` only — does NOT call `encounter.Resolve()` to persist `IsResolved=true` to DB |
| `WretchedWhispers.Api/Services/StagePluginRegistry.cs` | Maps stages to allowed KernelFunction lists | VERIFIED | Exists; `GetFunctionsForStage(SessionStage stage, Kernel kernel)` switch expression covering all 6 stages |
| `WretchedWhispers.Api/Prompts/NarratorPersona.cs` | Fixed narrator persona text | VERIFIED | Exists; `static class NarratorPersona` with `const string Text` containing doom-metal tone guidance |
| `WretchedWhispers.Api/Prompts/StagePrompts.cs` | Per-stage instruction fragments | VERIFIED | Exists; `static class StagePrompts` with `For(SessionStage)` returning non-empty string for all 6 stages |
| `WretchedWhispers.Api/Services/PromptComposer.cs` | Composes system prompt from 3 fragments | VERIFIED | Exists; `sealed class PromptComposer` with `Compose(SessionContext)` returning persona + stage + snapshot |
| `WretchedWhispers.Api/Services/GameSessionService.cs` | Rewritten orchestration using stage machine | VERIFIED | Contains `SessionContext`, `BuildSessionContextAsync`, wrapper plugin imports, StageTransitionFilter, FunctionChoiceBehavior.Auto(functions:), combat routing |
| `WretchedWhispers.Api/Plugins/CombatAgent/CombatAgentService.cs` | Combat sub-agent orchestration | VERIFIED | Exists; `sealed class CombatAgentService` with `ResolveCombat(...)` and `MaxIterations = 30` |
| `WretchedWhispers.Api/Prompts/CombatPrompts.cs` | Combat-specific agent instructions | VERIFIED | Exists; `static class CombatPrompts` with `Instructions` const and `ComposeWithContext(SessionContext)` method |
| `WretchedWhispers.Tests/StateMachine/StageDerivationTests.cs` | 12+ tests covering all 6 stages | VERIFIED | 12 test methods |
| `WretchedWhispers.Tests/StateMachine/StageTransitionTests.cs` | 7+ tests for transitions and filter | VERIFIED | Theory + Fact tests covering all 5 transitions, non-transitions, filter next() ordering |
| `WretchedWhispers.Tests/Plugins/WrapperPluginTests.cs` | 12+ tests for wrapper plugins | VERIFIED | 13 test methods |
| `WretchedWhispers.Tests/StateMachine/StagePluginRegistryTests.cs` | 6+ tests (one per stage) | VERIFIED | 7 test methods |
| `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs` | 6+ tests for prompt composition | VERIFIED | 8 test methods |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `SessionContext` | Campaign, Character, Encounter domain entities | `DeriveStage()` reads domain state | VERIFIED | `Character.IsDead`, `Campaign.WorldEnded`, `Campaign.IsEnded`, `Campaign.IsActive()`, `ActiveEncounter.IsStarted/IsEnded/IsResolved` all read correctly |
| `StageTransitionFilter` | `SessionContext.DeriveStage()` | `OnAutoFunctionInvocationAsync` | VERIFIED | Filter calls `sessionContext.DeriveStage()` after `next(context)` |
| Wrapper plugins | `SessionContext` | Constructor DI reads/writes IDs | VERIFIED | `sessionContext.CharacterId/CampaignId/ActiveEncounterId` read; `Set*` mutators called on creation |
| Wrapper plugins | Original plugins (via interfaces) | Constructor DI delegation via `inner.` | VERIFIED | `ICharacterOperations`, `ICampaignOperations`, `IEncounterOperations`, `IDiceOperations` interfaces; adapters bridge to original plugins |
| `StagePluginRegistry` | `Kernel.Plugins.GetFunction` | Function lookup by plugin name | VERIFIED | `kernel.Plugins.GetFunction(plugin, func)` in `GetFunctions()` helper |
| `GameSessionService.BuildKernelForSession` | Wrapper plugins | `ImportPluginFromObject` | VERIFIED | Lines 391-407 of GameSessionService.cs import all 5 wrappers |
| `GameSessionService.CreateGameMasterAgent` | `PromptComposer.Compose` | `Instructions = promptComposer.Compose(sessionContext)` | VERIFIED | Line 467 of GameSessionService.cs |
| `GameSessionService` | `FunctionChoiceBehavior.Auto(functions:)` | `stagePluginRegistry.GetFunctionsForStage` | VERIFIED | Lines 461, 472-473 of GameSessionService.cs |
| `GameSessionService` | `CombatAgentService` | Called when stage is `SessionStage.Combat` | VERIFIED | Lines 108-113 of GameSessionService.cs |
| `ResolutionWrapperPlugin.CompleteResolution` | `Encounter.Resolve()` | Domain entity mutation to persist IsResolved | NOT WIRED | `CompleteResolution()` calls `sessionContext.ClearActiveEncounter()` only — never loads encounter from repo or calls `encounter.Resolve()` |

---

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `SessionContext.FormatSnapshot()` | `Character`, `Campaign`, `ActiveEncounter` | `BuildSessionContextAsync()` loads from repos | Yes — repository reads from SQLite | FLOWING |
| `PromptComposer.Compose()` | `SessionContext` | Per-turn `BuildSessionContextAsync` result | Yes — real domain objects | FLOWING |
| `StagePluginRegistry.GetFunctionsForStage` | `stage` from `sessionContext.DeriveStage()` | Real domain state | Yes | FLOWING |
| `ResolutionWrapperPlugin.CompleteResolution` | `Encounter.IsResolved` (for next turn) | Never written — `encounter.Resolve()` not called | No — IsResolved stays false in DB | DISCONNECTED |

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| All tests pass | `dotnet test WrtechedWhispers.sln --no-build -q` | Failed: 0, Passed: 315 | PASS |
| Build succeeds with no errors | `dotnet build WrtechedWhispers.sln` | Build succeeded, 0 Errors | PASS |
| Resolution plugin wires to encounter.Resolve() | Code inspection of ResolutionWrapperPlugin.cs | Method only calls `sessionContext.ClearActiveEncounter()` | FAIL |

---

### Requirements Coverage

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| MORK-01 | 07-01, 07-02, 07-03, 07-04 | Full session lifecycle from character creation through 7th Misery or death | PARTIAL | The lifecycle CharacterCreation→CampaignSetup→Exploration→Combat→Resolution is implemented, but the Resolution→Exploration transition is broken: the session gets stuck in Resolution stage after any combat because `IsResolved` is never persisted |

**Note on REQUIREMENTS.md traceability:** MORK-01 maps to Phase 6 in the traceability table. Phase 7 extends MORK-01 with a deterministic state machine. The traceability table was not updated to include Phase 7, but this is a documentation gap not a functional gap.

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `ResolutionWrapperPlugin.cs` | 16-22 | `CompleteResolution()` never calls `encounter.Resolve()` — only clears context reference | Blocker | Session stuck in Resolution stage after every combat encounter |
| `StageTransitionFilter.cs` | 9-31 | Filter validates transitions but takes no action (no logging, no enforcement) | Info | Intentional per research Pitfall 1 — observation-only for future extensibility |
| `GameSessionService.cs` | 27 | `IConfiguration configuration` parameter unused (CS9113) | Info | Pre-existing warning, no Phase 7 impact |

---

### Human Verification Required

#### 1. Full Gameplay Loop End-to-End

**Test:** Start the API, create a new session, play through: character-creation → campaign-setup → exploration → (create encounter with adversary) → combat resolution → verify stage returns to exploration after CompleteResolution call
**Expected:** Stage advances at each step; no GUIDs in narrative text; state_update SSE events include the `stage` field
**Why human:** Requires running server and live LLM interaction. The gap above (stuck in Resolution) will manifest here and confirm the bug.

---

### Gaps Summary

One gap blocks full goal achievement:

**Resolution → Exploration transition is broken.** `ResolutionWrapperPlugin.CompleteResolution()` clears the in-memory `SessionContext.ActiveEncounter` reference but does not write `IsResolved=true` to the database via `encounter.Resolve()`. On the next player turn, `BuildSessionContextAsync` (line 365: `enc.IsStarted && !enc.IsResolved`) re-loads the ended-but-unresolved encounter, `DeriveStage()` returns `Resolution` again, and the session is permanently trapped in Resolution stage after any combat.

**Fix required:** Inject `IEncountersRepository` into `ResolutionWrapperPlugin`, load the encounter by `sessionContext.ActiveEncounterId`, call `encounter.Resolve()`, save it back to the repository, then call `sessionContext.ClearActiveEncounter()`. Add a corresponding test verifying `IsResolved` is persisted.

All other phase components — state machine, wrapper plugins, prompt composition, function filtering, combat sub-agent — are fully implemented and wired.

---

_Verified: 2026-03-25T08:00:00Z_
_Verifier: Claude (gsd-verifier)_
