# WretchedWhispers.Evals

Behavioral evals for the AI game master, built on `Microsoft.Extensions.AI.Evaluation`. Unlike the unit
tests (scripted, deterministic), these drive the **real** model and **score** behavior. They skip
cleanly (not fail) when no credentials are configured, so the normal CI test run is unaffected.

## Running

Set the Azure OpenAI credentials the app uses (see `AgentConfiguration`). The evals read the normal
.NET configuration stack: `appsettings*.json`, user secrets, then environment variables.

```bash
dotnet user-secrets set --project wretched-whispers-server/WretchedWhispers.Evals AzureOpenAiSettings:Endpoint "https://<resource>.openai.azure.com/"
dotnet user-secrets set --project wretched-whispers-server/WretchedWhispers.Evals AzureOpenAiSettings:ApiKey "<key>"
dotnet user-secrets set --project wretched-whispers-server/WretchedWhispers.Evals AzureOpenAiSettings:ChatModelDeployment "<deployment>"

# or, in CI:
export AzureOpenAiSettings__Endpoint="https://<resource>.openai.azure.com/"
export AzureOpenAiSettings__ApiKey="<key>"
export AzureOpenAiSettings__ChatModelDeployment="<deployment>"

dotnet test wretched-whispers-server/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
```

Without these set, the evals **skip**. The first run hits Azure and writes a response cache under
`<solution root>/.eval-results/` (git-ignored, override with `WW_EVAL_RESULTS`); later runs replay
from the cache and are free + deterministic until a prompt, seed, or scenario changes.

## Reporting

Render an HTML report with the eval console tool:

```bash
dotnet tool install --global Microsoft.Extensions.AI.Evaluation.Console
dotnet aieval report --path wretched-whispers-server/.eval-results --output eval-report.html
```

See `docs/running-evals.md` for the full walkthrough.

## How a turn runs

`EvalHost` wires the production services over in-memory SQLite, and `EvalTurnRunner` drives each turn
through the real `TurnCoordinator` — unit of work, session lock, Ended-stage refusal, empty-response
handling and chat-history persistence included. Scenarios read the result from `TurnOutcome`:
`Narrative` (everything shown, in order) and `NarrativeBeforeTools` (the prose written before the first
tool result), `Calls` (tool names + JSON arguments, from the turn trace the coordinator persisted for that turn id,
committed or not), `ToolResults`, `Delta`, and `Error` (set when the coordinator
refused or failed the turn).

## Current evals

Suite `campaign-creation` (`CampaignCreationEvals.cs`):

- **Opening-NarratesRolledWretch** — the opening turn calls exactly `ConfigureCampaign` and narrates the pre-rolled character (name + class) from Game State.
- **Opening-DoesNotReAsk** — the opening narration must not re-ask for a name or class (both come from the create-session form; there is no `CreateCharacter` tool).

Suite `domain-authority` (`DomainAuthorityEvals.cs`):

- **Combat-PlayerAttack-OneRound** — a player attack must call `ResolveCombatRound` exactly once (journaling a notable event alongside it is permitted).
- **Combat-InventoryQuestion-NoTurn** — an in-combat inventory/equipment question must answer from state without calling combat tools.
- **Combat-MissingItemUse-NoTurn** — using a missing item in combat must not invent it or advance the round.
- **Combat-NoOutcomeBeforeTheRoll** — all narration is shown as it is written, so prose written before `ResolveCombatRound` returns must not state the attack's result (LLM judge over the pre-tool text; calling the tool first passes outright).
- **Exploration-ClaimedLoot-NeedsStateChange** — taking an item either really changes the inventory (the turn delta shows it) or is not narrated as taken (LLM judge when the delta shows no item).
- **Combat-DoubleAction-OneRound** — a player message describing two attacks is still one round: exactly one `ResolveCombatRound` call (code also rejects a second call in the same turn).
- **Combat-TwoAdversaries-TargetsByName** — with two named adversaries in the fight, a player attack on one "by description" must call `ResolveCombatRound` with that adversary's name as `targetAdversaryName`.
- **Combat-CastScroll-ThenRoundOther** — an in-combat scroll cast must call `CastScroll` then exactly one `ResolveCombatRound` (the 'Other' path).
- **Combat-OmenSpend-MaxDamage** — an explicit omen spend must ride `ResolveCombatRound`'s `omenUse` argument; the domain's committed omen count is the witness.
- **Combat-DeathFight-DeathIsFinal** — *multi-turn*: the player swings at an unwinnable foe until the seeded dice kill them, then tries to fight on — the coordinator must refuse that turn with an error before any model call, and no tool may run.
- **Combat-Narration-Grounded** — LLM-judge groundedness of combat narration against the tool results it's based on, threshold >= 4.
- **Exploration-MemorableNpc-Journaled** — meeting a memorable NPC and making a promise must trigger `RecordJournalEntry`.
- **Exploration-BuyItem-DeductsAndAdds** — a purchase must go through `BuyItem`, never narration alone.
- **Exploration-Rest-HealsViaRest** — resting must go through `Rest`, never narrated healing.
- **Exploration-CastScroll-SpendsUse** — casting a possessed scroll must go through `CastScroll`.
- **Exploration-TorchLit-UsesItem** — genuinely consuming a carried item must call `UseItemFromCharacterInventory`.
- **Exploration-OmenSpend-LowersDr** — an omen spent on an ability test must ride `ChallengeCharacter`'s `spendOmenToLowerDr` flag, proven by the committed omen count.
- **Exploration-RiskyFeat-CallsChallenge** — a risky feat with real stakes must be resolved by `ChallengeCharacter`, never narrated success or failure.
- **Exploration-Camp-NoFabricatedItemUse** — LLM-judge groundedness: camp narration must not invent item consumption or counts.
- **Exploration-CombatEntry-OrderedChain** — violence erupting must run `CreateEncounter -> AddAdversaryToEncounter -> StartEncounter`, in order.
- **Exploration-CombatEntry-StopsAtStart** — an attack that starts combat ends the turn at `StartEncounter`: no ability test or bare roll stands in for the attack, the stage is Combat with nothing resolved, and the narration (LLM judge) states no attack result and leaves the move to the player.
- **Exploration-FirstMeeting-RollsReaction** — an open-attitude first meeting must create the encounter as `Unknown` so the domain rolls the reaction table.
- **Resolution-Loot-AddsItem** — Resolution-stage loot must go through `AddItemToCharacterInventory`.
- **Resolution-MovingOn-Completes** — leaving the aftermath must call `CompleteResolution`, and the derived stage must be Exploration afterwards.

Tool results are deterministic (the model-facing DTOs carry no entity ids, and `EvalHost` seeds the
domain dice), so every completion in a turn — not just the first — cache-hits on re-runs. That makes
multi-turn and dice-dependent scenarios viable: **Combat-DeathFight-DeathIsFinal** runs a whole fight
to the death across several turns and still replays entirely from cache. Most scenarios stay
single-turn as focused regression guards.
