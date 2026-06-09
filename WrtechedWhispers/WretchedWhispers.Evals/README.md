# WretchedWhispers.Evals

Behavioral evals for the AI game master, built on `Microsoft.Extensions.AI.Evaluation`. Unlike the unit
tests (scripted, deterministic), these drive the **real** model and **score** behavior. Excluded from the
default CI test run.

## Running

Set the Azure OpenAI credentials the app uses (see `AgentConfiguration`):

```bash
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

- **CampaignCreation/Turn1-Begin** — "begin" must call no tools (asks for a name).
- **CampaignCreation/Turn2-Name** — a name must trigger `CreateCharacter -> ConfigureCampaign -> StartCampaign`, in that exact order.
