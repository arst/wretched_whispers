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

## Current evals

Suite `campaign-creation` (`CampaignCreationEvals.cs`):

- **Opening-NarratesRolledWretch** — the opening turn calls exactly `ConfigureCampaign` and narrates the pre-rolled character (name + class) from Game State.
- **Opening-DoesNotReAsk** — the opening narration must not re-ask for a name or class (both come from the create-session form; there is no `CreateCharacter` tool).

Suite `domain-authority` (`DomainAuthorityEvals.cs`):

- **Combat-PlayerAttack-OneRound** — a player attack must call `ResolveCombatRound` exactly once (journaling a notable event alongside it is permitted).
- **Combat-InventoryQuestion-NoTurn** — an in-combat inventory/equipment question must answer from state without calling combat tools.
- **Combat-MissingItemUse-NoTurn** — using a missing item in combat must not invent it or advance the round.
- **Combat-CastScroll-ThenRoundOther** — an in-combat scroll cast must call `CastScroll` then exactly one `ResolveCombatRound` (the 'Other' path).
- **Combat-OmenSpend-MaxDamage** — an explicit omen spend must ride `ResolveCombatRound`'s `omenUse` argument; the domain's committed omen count is the witness.
- **Combat-DeathFight-DeathIsFinal** — *multi-turn*: the player swings at an unwinnable foe until the seeded dice kill them, then tries to fight on — the Ended stage must expose no tools and the narration must refuse the revival.
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
- **Exploration-FirstMeeting-RollsReaction** — an open-attitude first meeting must create the encounter as `Unknown` so the domain rolls the reaction table.
- **Resolution-Loot-AddsItem** — Resolution-stage loot must go through `AddItemToCharacterInventory`.
- **Resolution-MovingOn-Completes** — leaving the aftermath must call `CompleteResolution`, and the derived stage must be Exploration afterwards.

Tool results are deterministic (the model-facing DTOs carry no entity ids, and `EvalHost` seeds the
domain dice), so every completion in a turn — not just the first — cache-hits on re-runs. That makes
multi-turn and dice-dependent scenarios viable: **Combat-DeathFight-DeathIsFinal** runs a whole fight
to the death across several turns and still replays entirely from cache. Most scenarios stay
single-turn as focused regression guards.
