# Running the evals

The `WretchedWhispers.Evals` project holds live LLM evals for the game master pipeline. They are
plain xUnit facts: without Azure OpenAI credentials every live eval skips cleanly (the harness's own
deterministic tests still run — CI runs those on every push).

## Credentials

Provide `AzureOpenAiSettings` (`Endpoint`, `ApiKey`, `ChatModelDeployment`) via user secrets
(`WretchedWhispers.Console-dev`), appsettings, or environment variables — same shape as the API.

```bash
dotnet test wretched-whispers-server/WretchedWhispers.Evals
```

## Results and reports

Results and the model-response cache land in `wretched-whispers-server/.eval-results/` (next to the
solution file; gitignored; override with `WW_EVAL_RESULTS`). Each test run is one *execution* —
named by `GITHUB_SHA` in CI, timestamp locally — so runs are comparable over time.

Render the HTML report with the `aieval` tool:

```bash
dotnet tool install -g Microsoft.Extensions.AI.Evaluation.Console
aieval report --path wretched-whispers-server/.eval-results --output eval-report.html
```

## Notes

- Model responses (game turn AND judge calls) are cached on disk, so re-running unchanged scenarios
  is free and deterministic. Changing a prompt, a seed, or a scenario invalidates its cache entries
  and the next run goes live.
- Pass/fail bars live in `EvalSupport` (metric interpreter + `GroundednessPassBar`), so the stored
  metrics carry the same red/green the asserts enforce.
