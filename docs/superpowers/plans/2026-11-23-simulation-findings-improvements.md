# Simulation Findings Improvement Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the failure modes the first simulated-player run found, code gaps first, then prompt tuning with an eval for each behaviour tuned.

**Architecture:** Four stages. Stage 1 closes gaps in the domain and the tool surface, where the model cannot do the right thing however it is prompted. Stage 2 re-runs the simulation to confirm those fixes and to measure the prompt problems again with the new tools in place. Stage 3 tunes prompts, each change led by an eval that fails first. Stage 4 confirms with a third run.

**Tech Stack:** .NET 10 (`WretchedWhispers.Core`, `.Engine`, `.Api`, `.Tests`, `.Evals`), xunit v3, `Microsoft.Extensions.AI.Evaluation`, the simulation tooling in `simulation/`.

**Spec:** `simulation/findings/2026-11-23-first-run.md`. Item numbers below ("finding 1") refer to its ranking.

## Global Constraints

- One task is one branch and one pull request off `main`. Commit or push only when asked.
- Every task is test-first: write the named test, see it fail, implement, see it pass, run the whole suite, commit.
- Build: `dotnet build wretched-whispers-server/WretchedWhispers.slnx`. Test: `dotnet test --solution wretched-whispers-server/WretchedWhispers.slnx`. CI enforces `dotnet format`.
- The game state changes only through tools; a tool result is the only source of what happened.
- Tool DTOs carry no entity ids (the eval response cache depends on it).
- No null-forgiving `!` on non-nullable values. No overloads that exist only for tests.
- Characters, encounters and campaigns are stored as JSON. A new field must deserialize from rows written before it existed (give it a default). None of the tasks below changes a table, so no migration is needed; if one does, it needs both the SQLite and the Postgres migration.
- Stage 1 changes code and tool descriptions only. `NarratorPersona.cs` and `StagePrompts.cs` are not edited until Stage 3.
- Any change to the order or number of dice rolls invalidates cached eval responses for scenarios that reach it. Re-record them in the same pull request and say so in its description.

## Review Focus

Conditions the findings imply that are easy to miss; each has a test in the task that owns it.

1. A character saved before this work (scrolls named `random`, items with no effect field) still loads and plays. Tasks 3 and 4.
2. The player sells more units than they carry: nothing is removed and no silver is paid. Task 2.
3. An item whose real name ends in ` x<number>` (the model may create one) still resolves to itself. Task 1.
4. A new tool is offered in exactly the stages intended; `SellItem` and `GainSilver` are not offered in combat. Task 2.
5. Damage that breaks the character during a scroll fizzle reports the injury too, not only tests and combat rounds. Task 5.

---

## Stage 1: Domain and tool gaps

Nine tasks, independent unless a dependency is named.

### Task 1: Item names resolve the way Game State shows them (finding 10)

Game State prints `- life elixir x2` (`SessionContext.FormatSnapshot`), the model copies the quantity into the name, and `UseItemFromCharacterInventory` matches the description exactly. 13 of 131 calls failed this way.

**Files:**
- Modify: `WretchedWhispers.Engine/GameTools/CharacterTools.cs`
- Test: `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`

**Interfaces:**
- Produces: `private static InventoryItem? FindItem(Character character, string itemDescription)` in `CharacterTools`. Exact case-insensitive match first; if none, match again after removing one trailing ` x<digits>` from `itemDescription`. Task 2 uses it.

- [ ] Test `UseItem_AcceptsTheNameWithItsQuantitySuffix`: a character carrying `life elixir` ×2; calling the tool with `"life elixir x2"` leaves quantity 1.
- [ ] Test `UseItem_PrefersAnExactNameOverAStrippedOne`: carrying both `bolts x20` and `bolts`; calling with `"bolts x20"` consumes from `bolts x20`.
- [ ] Implement `FindItem`, use it in `UseItemFromCharacterInventory`. The not-found error keeps listing current items.
- [ ] Run the suite, commit.

### Task 2: Silver can rise (finding 1)

No tool adds silver, so sales and loot never pay and the model adds "silver coin" items instead.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs`
- Modify: `WretchedWhispers.Engine/GameTools/CharacterTools.cs`
- Test: `WretchedWhispers.Tests/Characters/Possessions/` (new `SilverTests.cs`), `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`, `WretchedWhispers.Tests/Services/GameToolCatalogTests.cs`

**Interfaces:**
- Consumes: `FindItem` from Task 1.
- Produces:
  - `public void GainSilver(int amount)` on `Character`; throws on zero or negative.
  - `public void SellItem(Guid itemId, int price, int quantity = 1)` on `Character`; `price` is the total paid, zero allowed (given away); throws without changing anything when fewer than `quantity` are carried.
  - Tool `SellItem(string itemDescription, int silverPrice, int quantity = 1)`, stages Exploration and Resolution, returns `CharacterDto`. Description: sell, trade away or give away a carried item; removes it and pays the silver in one step.
  - Tool `GainSilver(int amount, string source)`, stages Exploration and Resolution, returns `CharacterDto`. Description: silver found, looted, paid or rewarded; `source` is one short phrase saying where it came from (kept in the trace).
  - `AddItemToCharacterInventory` description gains: "Never for coins or silver; GainSilver does that."

- [ ] Tests on `Character`: `GainSilver_AddsToSilver`; `SellItem_RemovesTheUnitsAndPaysThePrice`; `SellItem_MoreThanCarried_ThrowsAndChangesNothing`.
- [ ] Tests on the tools: `SellItem_ResolvesTheItemByDescription`; `GainSilver_RejectsZeroAndNegative` (through `ToolGuard.Positive`).
- [ ] Catalog test: both tools are offered in Exploration and Resolution and not in Combat.
- [ ] Implement, run the suite, commit.

### Task 3: Scrolls have names and effects, applied by the game (finding 9)

Starting scrolls are created with the description `"random"` at three sites in `CharacterCreationService.cs` (lines 70, 72, 75). A cast "succeeds" with nothing defined.

**Files:**
- Create: `WretchedWhispers.Core/Characters/Possessions/Scrolls/ScrollPowers.cs`
- Modify: `WretchedWhispers.Core/Characters/Create/CharacterCreationService.cs`, `WretchedWhispers.Core/Characters/Cast/CastOutcome.cs`, `WretchedWhispers.Core/Characters/Character.cs` (`Cast`, `Challenge`), `WretchedWhispers.Core/Encounters/EncounterService.cs`, `WretchedWhispers.Engine/GameTools/CharacterTools.cs` (`CastScroll`), `WretchedWhispers.Engine/Services/SessionContext.cs` (`FormatSnapshot`)
- Create: `WretchedWhispers.Engine/GameTools/Models/CastOutcomeDto.cs`
- Test: `WretchedWhispers.Tests/Characters/Powers/ScrollPowerTests.cs` (new), `WretchedWhispers.Tests/Characters/Create/`, `WretchedWhispers.Tests/Encounters/`, `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`

**Interfaces:**
- Produces:
  - `public enum ScrollEffectKind { Narrative, HealCaster, BlessNextTest, DamageEach, DamageSplit }`.
  - `public sealed record ScrollPower(string Name, ScrollSchool School, string Effect, ScrollEffectKind Kind = ScrollEffectKind.Narrative, DiceExpr? Amount = null, DiceExpr? Targets = null)`.
  - `public static ScrollPower ScrollPowers.Roll(ScrollSchool school, Dice dice)` and `public static ScrollPower? ScrollPowers.Find(string name)`: the book's two d10 tables, ten Unclean and ten Sacred, effects in our own words. A scroll's `Description` is the power's name; the power is looked up by it, so `Scroll` gains no field and a saved `random` scroll simply has no power.
  - `Character.Cast` applies what touches the caster on a success: `HealCaster` heals `Amount`; `BlessNextTest` stores `Amount` as `NextTestBonus`, which lowers the DR of the character's next test and is then cleared. `CastOutcome` gains `string? Effect`, `int HpHealed`, `int NextTestBonus`.
  - `public Task<ScrollCastResult> EncounterService.CastScroll(Guid characterId, Guid scrollId, Guid? encounterId, CancellationToken ct = default)`: casts, then for `DamageEach` and `DamageSplit` damages the active adversaries of a started, unfinished encounter. `ScrollCastResult(CastOutcome Outcome, IReadOnlyList<ScrollHit> Hits)`, `ScrollHit(string AdversaryName, int Damage, bool Killed)`. It does not end the encounter: the round that follows (`ResolveCombatRound` with `Other`) does.
  - Tool `CastScroll` returns `CastOutcomeDto(bool Succeeded, string Reason, string? Scroll, string? Effect, int HpLost, int HpHealed, int NextTestBonus, IReadOnlyList<ScrollHitDto> AdversariesHit, int CurrentHp)`.
  - Game State line becomes `- Scroll: {Description} ({School}): {Effect}`, without the trailing part when the scroll has no power; a pending bonus shows as `Blessed: +N on the next test`.

Which powers the game applies: Grace of a Dead Saint (heal d10), Aegis of Sorrow (heal 2d6), Grace for a Sinner (+d6 on the next test), Palms Open the Southern Gate (d2 adversaries, d8 each), Nine Violet Signs Unknot the Storm (d2 adversaries, d6 each), Roskoe's Consuming Glare (d4 adversaries, d8 each), Daemon of Capillaries (one adversary, 2d4), Death (4d10 split among all). The other twelve are narrative.

- [ ] Test `Create_NeverNamesAScrollRandom`: over seeded rolls that produce scrolls, every scroll's description is a known power.
- [ ] Test `Cast_HealingPower_HealsTheCaster` and `Cast_Blessing_LowersTheNextTestOnce`.
- [ ] Test `CastScroll_DamagingPower_HurtsTheAdversariesOfTheActiveFight` and `CastScroll_DamagingPower_WithNoFight_HitsNothing`.
- [ ] Test `CastScroll_KillingEveryAdversary_LeavesTheRoundToEndTheFight`: after the cast the encounter is still started; `ResolveRound` with `Other` ends it as `AllDefeated`.
- [ ] Test `Cast_ScrollNamedRandom_StillSucceedsWithNoEffect` (a character saved before this work).
- [ ] Implement, run the suite, re-record any eval that casts a scroll, commit.

### Task 4: Healing items heal (finding 10)

Using an item only removes it; a life elixir heals nothing.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/InventoryItem.cs`, `WretchedWhispers.Core/Characters/Character.cs`, `WretchedWhispers.Core/Characters/Create/CharacterCreationService.cs`, `WretchedWhispers.Engine/GameTools/CharacterTools.cs`
- Create: `WretchedWhispers.Engine/GameTools/Models/ItemUseOutcomeDto.cs`
- Test: `WretchedWhispers.Tests/Characters/Possessions/`, `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`

**Interfaces:**
- Produces:
  - `public enum ItemEffect { None, Heal }`; `InventoryItem.Effect`, a constructor parameter defaulting to `None`.
  - `public ItemUseOutcome UseItem(Guid itemId, Dice dice)` on `Character`, replacing the tool's direct `ConsumeItem` call. `ItemUseOutcome(int HpHealed, bool InfectionCured)`. `Heal` restores d6 HP and cures infection, as the book gives for both the life elixir and the medicine chest.
  - `life elixir` and `medicine chest (Presence + 4 uses)` are created with `ItemEffect.Heal`.
  - `UseItemFromCharacterInventory` returns `ItemUseOutcomeDto(int HpHealed, bool InfectionCured, CharacterDto Character)`.
  - `BuyItem` and `AddItemToCharacterInventory` gain `bool heals = false`. The flag only labels the item; the healing itself is applied by `Character.UseItem`.

- [ ] Test `UseItem_HealingItem_RestoresHpAndCuresInfection` (seeded d6, capped at max HP).
- [ ] Test `UseItem_OrdinaryItem_HealsNothing`.
- [ ] Test `Item_SavedWithoutAnEffect_LoadsAsNone`.
- [ ] Implement, run the suite, re-record evals that use an item (`Exploration-TorchLit-UsesItem`, the combat item paths), commit.

### Task 5: Tool results name the injury (finding 7)

`ChallengeOutcomeDto` and `CombatRoundOutcomeDto` report damage and death but not which injury the Broken table gave, so the narrator cannot tell it. 8 of 18 injuries went untold.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs`, `WretchedWhispers.Core/Characters/CharacterService.cs` (`ChallengeResult`), `WretchedWhispers.Core/Encounters/CombatRound.cs`, `WretchedWhispers.Core/Characters/Cast/CastOutcome.cs`, `WretchedWhispers.Engine/GameTools/Models/ChallengeOutcomeDto.cs`, `WretchedWhispers.Engine/GameTools/Models/CombatRoundOutcomeDto.cs`
- Test: `WretchedWhispers.Tests/Characters/Challenge/`, `WretchedWhispers.Tests/Encounters/ResolveRoundTests.cs`, `WretchedWhispers.Tests/Characters/Powers/`

**Interfaces:**
- Produces: `InjuryKind? NewInjury` on `ChallengeResult`, `CombatRoundOutcome` (as `PlayerNewInjury`) and `CastOutcome`; `string? NewInjury` on `ChallengeOutcomeDto` and `string? PlayerNewInjury` on `CombatRoundOutcomeDto`, using the same words Game State uses for injuries (`SessionContext.FormatInjuries`). Task 6 reads these.

- [ ] Test `Challenge_DamageThatBreaks_ReportsTheInjury` (seeded: failed test, damage to 0, Broken table gives an injury).
- [ ] Test `ResolveRound_RetaliationThatBreaks_ReportsTheInjury`.
- [ ] Test `Cast_FizzleThatBreaks_ReportsTheInjury`.
- [ ] Test `Challenge_NoBreak_ReportsNoInjury`.
- [ ] Implement, run the suite, commit.

### Task 6: A broken character gets back up (finding 6)

Today a character who survives the Broken table stays at 0 HP indefinitely and every later hit rolls the table again. 39 turns began at 0 HP. In the book a broken character wakes with d4 HP.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs` (`ReceiveDamage`), `WretchedWhispers.Engine/GameTools/Models/ChallengeOutcomeDto.cs`, `WretchedWhispers.Core/Encounters/CombatRound.cs` (where `PlayerBroken` is computed)
- Test: `WretchedWhispers.Tests/Characters/Status/`, `WretchedWhispers.Tests/Encounters/ResolveRoundTests.cs`

**Interfaces:**
- Consumes: `NewInjury` from Task 5.
- Produces: after a survived Broken roll, HP is set to a d4 roll. `IsBroken` and `PlayerBroken` mean "an injury was rolled in this call" (`NewInjury is not null`), no longer "HP is 0 and alive".

- [ ] Test `SurvivingBroken_LeavesBetweenOneAndFourHp`.
- [ ] Test `SurvivingBroken_StillReportsBroken` on both DTOs.
- [ ] Test `DyingOnTheBrokenTable_StaysAtZeroAndDead`.
- [ ] Implement, run the suite, re-record the evals whose dice reach a Broken roll (`Combat-DeathFight-DeathIsFinal` at least), commit.

### Task 7: A successor starts outside the dead character's fight (finding 8)

`CreateSuccessor` (`SessionEndpoints.cs:292`) buries the character but leaves the encounter unresolved, so `SessionContextLoader` hands it to the successor and the stage is Combat.

**Files:**
- Modify: `WretchedWhispers.Core/Encounters/Encounter.cs`, `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs`
- Test: `WretchedWhispers.Tests/Sessions/SessionEndpointTests.cs`, `WretchedWhispers.Tests/Encounters/`

**Interfaces:**
- Produces: `public void Abandon()` on `Encounter`: ended and resolved from any state. `CreateSuccessor` calls it on every unresolved encounter of the campaign before the successor joins.

- [ ] Test `Abandon_FromAStartedFight_IsEndedAndResolved`.
- [ ] Test `Successor_AfterDeathInCombat_StartsInExploration`: die mid-fight, create the successor, the loaded context has no active encounter and the derived stage is Exploration.
- [ ] Implement, run the suite, commit.

### Task 8: The prompt hash identifies the prompt, not the turn

`promptHash` covers the instructions including Game State, so it differs on almost every turn and runs cannot be compared by prompt version. Stage 3 needs that comparison.

**Files:**
- Modify: `WretchedWhispers.Engine/Services/PromptComposer.cs`, `WretchedWhispers.Engine/Services/AgentExecutor.cs`
- Test: `WretchedWhispers.Tests/Prompts/PromptComposerTests.cs`
- Modify: `simulation/README.md` (drop the "does not identify a prompt version" caveat)

**Interfaces:**
- Produces: `public string VersionHash(SessionContext context)` on `PromptComposer`: SHA-256 of everything `Compose` writes before `## Game State`. `AgentExecutor` records it as `PromptHash`.

- [ ] Test `VersionHash_SameStageDifferentState_IsTheSame` (two contexts differing only in HP).
- [ ] Test `VersionHash_DifferentStage_Differs`.
- [ ] Implement, run the suite, commit.

### Task 9: Weapon, armor and shield can change (finding 4)

No tool changes what the character wields or wears, so looted mail or a bought sword can only be narrated.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs`, `WretchedWhispers.Engine/GameTools/CharacterTools.cs`
- Test: `WretchedWhispers.Tests/Characters/Possessions/EquipmentTests.cs` (new), `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`, `WretchedWhispers.Tests/Services/GameToolCatalogTests.cs`

**Interfaces:**
- Produces:
  - On `Character`: `public void Equip(Weapon weapon, int price = 0)`, `public void Wear(Armor armor, int price = 0)`, `public void TakeUpShield(int price = 0)`. Each replaces what was there; a price above the character's silver throws and changes nothing.
  - Tools `EquipWeapon(WeaponKind weapon, int silverCost = 0)`, `EquipArmor(ArmorTier armor, int silverCost = 0)`, `EquipShield(int silverCost = 0)`, stages Exploration and Resolution, each returning `CharacterDto`. `EquipWeapon` refuses `Fangs` and `Claws` (natural attacks are not gear).

- [ ] Tests on `Character`: `Equip_ReplacesTheWeapon`; `Wear_ReplacesTheArmor`; `TakeUpShield_GivesAShield`; `Equip_WithAPrice_DeductsSilver`; `Equip_PriceAboveSilver_ThrowsAndChangesNothing`.
- [ ] Test `Wear_HeavyArmor_BlocksScrolls` (the existing restriction now reachable in play).
- [ ] Test `EquippedGear_SurvivesSaveAndLoad` (JSON round trip of weapon, armor and shield).
- [ ] Tool tests: `EquipWeapon_RefusesNaturalAttacks`; catalog: offered in Exploration and Resolution, not Combat.
- [ ] Implement, run the suite, commit.

**Stage 1 is done when** all nine are merged and the suite and cached evals are green.

---

## Stage 2: Measure again

No product code. One wave of the same eleven personas, five players at a time, on a clean simulation database.

- [ ] Extend `simulation/analysis/analyze.py` with the checks the Stage 1 fixes need: turns where silver rose; sell turns that paid; add-item calls for coins; use-item calls that failed on the name; turns begun at 0 HP; new injuries named in that turn's narration; scrolls named `random`; successors whose first turn is in Combat.
- [ ] Run the wave, export, analyse.
- [ ] Write `simulation/findings/<date>-second-run.md` with a before/after table against the first run.

**Expected:** silver rises on sell and loot turns; no coin items; name failures near zero; no scroll named `random`; no successor opens in combat; no turn begins at 0 HP if Task 6 went in. Anything that misses goes back to Stage 1 before prompt work starts.

The second run is also the baseline for Stage 3: new tools change what the model does, so the first run's rates for the prompt problems are not the ones to tune against.

---

## Stage 1b: what the second run sent back

From `simulation/findings/2026-11-23-second-run.md`. One branch, one commit per task, with Tasks 10 and 11 below.

### Task 12: Silver is capped per gain, and not handed out on demand

`GainSilver(1000, "admin transfer")` went through when a player claimed to be the admin. Decision D is reversed: there is a cap, and the prompt is tuned too, because a cap alone still allows many small grants.

- [ ] Tests: `GainSilver_AboveTheCap_ThrowsAndChangesNothing`; `GainSilver_AtTheCap_Pays`; `SellItem_PriceAboveTheCap_ThrowsAndKeepsTheItem`.
- [ ] `Character.MaxSilverPerGain = 100`, enforced in `GainSilver` and `SellItem`. `GainSilver` is limited to two calls a turn, so a refused sum cannot be split in the same turn.
- [ ] Eval first: `Exploration-OutOfStoryDemand-NoSilver` (the admin message; no `GainSilver` call) and `Exploration-InventedDebt-NoSilver` (a claimed debt pays nothing). Record the rate against the current prompt, then change the `GainSilver` description and the Exploration prompt, and re-run. This is plan item 3.3's refusal half, brought forward.

### Task 13: A cast finds the scroll from its Game State line

- [ ] Test: `CastScroll_GivenTheGameStateLine_FindsTheScroll` (`"Scroll: Hermetic Step (Sacred): For 2d10 minutes…"` resolves to `Hermetic Step`); the exact name still wins.
- [ ] The lookup falls back to the scroll whose name the given text contains.

### Task 14: The wielded weapon can be sold

- [ ] Tests: `SellItem_NamingTheWieldedWeapon_PaysAndDisarms`; a carried item of the same name is sold first.
- [ ] `Character.SellWeapon(int price)`: pays, and the character falls back to the class's natural weapon or `Improvised`. `SellItem` uses it when nothing carried matches and the name is the wielded weapon's. Armor and shield are left for when a run needs them.

### Task 15: A round that kills the player says so

- [ ] Test: `ResolveCombatRound_ThatKillsThePlayer_ReportsPlayerDead`.
- [ ] `CombatRoundOutcomeDto` gains `PlayerDead`.

---

## Before Stage 3: two fixes found during Stage 1

Both predate Stage 1 and were left out of it. They are done with Stage 1b, and both must be merged before Stage 3 starts. The second run widened Task 10: a rest that heals must report it too.

### Task 10: Resting while infected says what it did

`Character.Rest` damages an infected character, and that damage can break or kill them. `AdvanceTimeOutcome` carries neither the damage, the injury nor the death, so the `Rest` tool's result is silent and the narrator can only learn it from Game State on the next turn.

**Files:**
- Modify: `WretchedWhispers.Core/Characters/Character.cs` (`Rest`), `WretchedWhispers.Core/Campaigns/Time/AdvanceTimeOutcome.cs`, `WretchedWhispers.Core/Campaigns/CampaignService.cs` (`AdvanceTimeWithRest`), `WretchedWhispers.Engine/GameTools/CampaignTools.cs` (`Rest`)
- Test: `WretchedWhispers.Tests/Characters/Status/`, `WretchedWhispers.Tests/GameTools/GameToolsTests.cs`

- [ ] Tests first, with a repository that round-trips JSON: `Rest_WhileInfected_ReportsTheDamage`; `Rest_WhileInfected_ThatBreaks_NamesTheInjury`; `Rest_WhileInfected_ThatKills_ReportsDeath`; `Rest_WhenHealthy_ReportsHpHealed`.
- [ ] The rest result carries HP healed or lost, the new injury (by `DisplayName()`, as Task 5 does) and whether the character died. Remove the `ponytail:` note on `ReceiveDamage` that records this gap.
- [ ] Run the suite, re-record the evals that rest, commit.

### Task 11: The eval suite stops failing at setup

Evals fail intermittently at host setup with `SqliteException` error 5 ("unable to delete/modify user-function due to active statements", "database is locked"). On `main` at `dd691ca` it happened in 4 of 4 runs of the suite, one or two evals each, a different eval each time. Stage 3 scores behaviours as rates over the evals, so a setup failure that looks like a behaviour failure spoils the numbers.

**Files:**
- Modify: `WretchedWhispers.Evals/Harness/EvalHost.cs` (opens `SqliteConnection("DataSource=:memory:")` per eval)

- [ ] Reproduce first: run the suite until it fails and keep the stack trace. Use superpowers:systematic-debugging; find the cause before changing anything.
- [ ] Untested lead: pooled in-memory connections shared between evals running side by side. `Pooling=False` on the eval connection, or a named private in-memory database per host, would be the fix if that is the cause.
- [ ] Done when the suite runs 10 times in a row from the cache with no setup failure.

---

## Stage 3: Prompt tuning, eval first

Each item is one pull request holding the eval, the prompt change, and its line in `WretchedWhispers.Evals/README.md`. The loop for each:

1. Add the eval from real trace samples. Run it against the current prompt and record the failing rate.
2. Change the prompt (or a tool description).
3. Re-run that eval and the whole suite; a fix that moves another eval is not done.

Behaviours that vary run to run are scored as a rate against a bar, the way `narrator-voice` already is. Exact wording is decided in each task from the second run's traces; the order below follows the findings' ranking.

| # | Behaviour (finding) | New eval coverage | Where the prompt change goes |
|---|---|---|---|
| 3.1 | Machinery talk when the player asks a question (2) | `narrator-voice` gains question turns: a rules question, "what happens if I use X", a healing question, an inventory question, in and out of combat. Three samples each, same `EngineTalk` check and judge, same style of bar. | Persona output rules |
| 3.2 | Run-on planning after the reply has ended (3) | A harness-level exact check on every eval's narration for the leak's markers ("The question:", "The final output should", "the last user message"), so every existing sample tests it. Plus sample turns of the kind that triggered it: a one-word message, an omen spend. | Persona output rules. If it cannot be held at zero, a backstop in `OutputScrubber` is the code fallback. |
| 3.3 | Money is honest (1, 4) | `Exploration-SellItem-PaysSilver`, `Exploration-FoundCoins-GainSilver` (and no coin item), `Exploration-InventedDebt-NoSilver` (a claimed debt pays nothing). | Exploration prompt: the buy/loot rule gains selling and found silver |
| 3.4 | A failed roll is told as a failure; no invented rules (4) | `Exploration-FailedTest-ToldAsFailure` (seeded dice, judge), `Exploration-NoInventedMechanics` (judge: no damage, bonus or lasting effect that no tool result reports). | Exploration prompt |
| 3.5 | Injuries and death are told (6, 7) | `Exploration-Injury-Named`, `Combat-Injury-Named`, `Exploration-Death-ToldAsDeath` (seeded dice; the narration names the injury the result reports, and never says alive on a death). | Persona death rule; Combat prompt |
| 3.6 | Place and time are kept (5) | `Exploration-Arrival-SetsLocation`, `Exploration-Travel-AdvancesTime`, and an exact check that any hour or day stated in narration matches Game State. | Exploration prompt; tool descriptions of `SetPartyLocation` and `AdvanceTime` |
| 3.7 | Combat does not act for the player (8) | `Combat-NonAttackAction-NoStrikeNarrated`, `Successor-Opening-NoCombat`. | Combat prompt |
| 3.8 | Chained actions and "undo" (10) | `Exploration-ChainedActions-ResolvedOrNamed` (every part resolved or said to be left), `Exploration-Undo-NoDuplicates`. | Exploration prompt |
| 3.9 | Off-task requests, reply length for terse players, repeated paragraphs (10) | Decide first whether these are wanted; lowest priority. | Persona |

If 3.6 does not hold through prompting, the code options are a single tool that charts a place and moves the party there, and time that advances by rule. Decide when the eval numbers are in.

---

## Stage 4: Confirm

- [ ] A third wave, same personas, compared with the second run by prompt hash.
- [ ] `simulation/findings/<date>-third-run.md` with the three runs side by side.
- [ ] Anything still above its bar after tuning gets a code guard, not another prompt pass.

---

## Decisions made (23 November 2026)

| | Decision | Outcome |
|---|---|---|
| A | A character who survives the Broken table gets d4 HP back at once | Yes (Task 6) |
| B | The narrator may mark a bought or found item as healing | Yes, applied in the domain: the item carries the effect and `Character.UseItem` applies it (Task 4) |
| C | Scroll effects that move numbers are applied by the game | Yes (Task 3) |
| D | A cap on silver per sale or gain | No. Reversed after the second run: 100 silver per gain or sale, with the prompt tuned as well (Task 12) |
| E | Tools to change weapon, armor and shield | Yes (Task 9) |
