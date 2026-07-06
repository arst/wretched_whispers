# WretchedWhispers.Evals

Behavioral evals for the AI game master, built on `Microsoft.Extensions.AI.Evaluation`. Unlike the unit
tests (scripted, deterministic), these drive the **real** model and **score** behavior. Excluded from the
default CI test run.

## Running

Set the Azure OpenAI credentials the app uses (see `AgentConfiguration`). The evals read the normal
.NET configuration stack: `appsettings*.json`, user secrets, then environment variables.

```bash
dotnet user-secrets set --project WrtechedWhispers/WretchedWhispers.Evals AzureOpenAiSettings:Endpoint "https://<resource>.openai.azure.com/"
dotnet user-secrets set --project WrtechedWhispers/WretchedWhispers.Evals AzureOpenAiSettings:ApiKey "<key>"
dotnet user-secrets set --project WrtechedWhispers/WretchedWhispers.Evals AzureOpenAiSettings:ChatModelDeployment "<deployment>"

# or, in CI:
export AzureOpenAiSettings__Endpoint="https://<resource>.openai.azure.com/"
export AzureOpenAiSettings__ApiKey="<key>"
export AzureOpenAiSettings__ChatModelDeployment="<deployment>"

dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
```

Without these set, the evals **skip**. The first run hits Azure and writes a response cache under
`.eval-results/` (git-ignored); later runs replay from the cache and are free + deterministic.

## Reporting

Results are stored under `.eval-results/` relative to the test binary output directory. Render an HTML
report with the eval console tool:

```bash
dotnet tool install --global Microsoft.Extensions.AI.Evaluation.Console
dotnet aieval report --path WrtechedWhispers/WretchedWhispers.Evals/bin/Debug/net10.0/.eval-results --output eval-report.html
```

## Current evals

- **CampaignCreation-Turn1-Begin** — "begin" must call no tools (asks for a name).
- **CampaignCreation-Turn2-Name** — a name must trigger `CreateCharacter -> ConfigureCampaign`, in that exact order (the campaign auto-starts once configured).
- **Combat-InventoryQuestion-NoTurn** — an in-combat inventory/equipment question must answer from state without calling combat tools.
- **Combat-MissingItemUse-NoTurn** — using a missing item in combat must not invent it or advance the round.
- **Combat-PlayerAttack-OneRound** — a player attack must call `ResolveCombatRound` exactly once (journaling a notable event alongside it is permitted).
- **Exploration-MemorableNpc-Journaled** — meeting a memorable NPC and making a promise must trigger `RecordJournalEntry`.
- **Combat-Narration-Grounded** — LLM-judge groundedness of combat narration against the tool results it's based on, threshold >= 4.
