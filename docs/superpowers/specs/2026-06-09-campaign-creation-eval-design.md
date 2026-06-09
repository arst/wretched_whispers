# Campaign-creation tool-call-order eval (Microsoft.Extensions.AI.Evaluation)

**Date:** 2026-06-09
**Status:** Approved (brainstorming), pending implementation plan

## Problem

The app's behavioral bugs (model failing to advance stages, fabricating combat) are exactly the
kind of regression a scripted unit test cannot catch but a real-model eval can. We want to start an
eval suite with **one simple, deterministic eval**: does the real model, driven through the app's
CharacterCreation-stage agent, create a campaign by calling the right tools in the right order?

Per `StagePrompts.CharacterCreation` the flow is two turns:

- Turn 1 (player: `"begin"`) → the model should ask for a character name and call **no tools**
  (the known "begin shouldn't create a character" failure mode).
- Turn 2 (player: a name) → the model should call **`CreateCharacter` → `ConfigureCampaign` →
  `StartCampaign`**, in that exact order.

## Decisions (locked during brainstorming, 2026-06-09)

1. **Eval target:** the **real** Azure OpenAI model, with **response caching** so reruns are free and
   reproducible. First run (with creds) records to a local cache; later runs replay. Skips when no
   creds. This is what makes it an *eval* (measures real behavior) rather than a second unit test.
2. **Seam under eval:** drive the real `AgentExecutor.ExecuteAsync` per turn (not the full HTTP/SSE
   loop, not a parallel `IChatClient` harness). Tool-call order is read from the `ToolResult`
   `GameTurnEvent`s the executor already emits.
3. **Scenario scope:** the full 2-turn flow (turn 1 asserts zero tools; turn 2 asserts the ordered
   triple).
4. **Location:** a new `WretchedWhispers.Evals` xUnit project, kept out of the normal `dotnet test`
   CI path.

## API caveat

Exact `Microsoft.Extensions.AI.Evaluation` API names below (`DiskBasedReportingConfiguration.Create`,
`CreateScenarioRunAsync`, `ScenarioRun.ChatConfiguration`, `EvaluationContext`, `BooleanMetric`, the
`IEvaluator.EvaluateAsync` parameter list, the `enableResponseCaching` switch) are taken from the
library's documented shape but must be **verified against the installed package version during
implementation** (Context7 `/dotnet/extensions` + the package's public surface). The architecture
below does not change if a name or parameter differs; only the binding does.

## Design

### Project & packages

New xUnit project `WretchedWhispers.Evals`:

- **Packages:** `Microsoft.Extensions.AI.Evaluation` (core abstractions + metrics),
  `Microsoft.Extensions.AI.Evaluation.Reporting` (response caching + result storage). NOT
  `.Quality`/`.Safety`/`.NLP` — those are LLM-judge / text-metric evaluators irrelevant to a
  structural tool-order check (YAGNI).
- **References:** `WretchedWhispers.Api`, `WretchedWhispers.Infrastructure`, `WretchedWhispers.Core`.
  Does NOT reference `WretchedWhispers.Tests`; the small in-memory-DB harness is replicated locally.
- **Layout:**
  - `Harness/EvalHost.cs` — builds in-memory SQLite `WretchedWhispersDbContext` + real Core services +
    `AgentToolProvider` + `SessionContextLoader` + `PromptComposer` + in-memory chat-history store.
    Mirrors the existing integration-test setup, but parameterized by the `IChatClient` to use.
  - `Harness/EvalTurnRunner.cs` — runs one CharacterCreation turn and returns the ordered tool-call
    names.
  - `Evaluators/ExpectedToolCallOrderContext.cs` — `EvaluationContext` carrying the expected order.
  - `Evaluators/ToolCallOrderEvaluator.cs` — the custom `IEvaluator`.
  - `CampaignCreationEvals.cs` — the `[Fact]` scenarios + reporting-configuration wiring.
  - `README.md` — how to run (env vars) and how to render the report (`dotnet aieval`).
- **Results/cache dir** `WretchedWhispers.Evals/.eval-results/` is **git-ignored** (local-only cache,
  not a committed CI artifact).

### Harness

`EvalHost` assembles production wiring once: an in-memory SQLite context (shared-connection pattern
from `SqliteTestBase`), real Core services so the campaign tools genuinely succeed, the real
`AgentToolProvider`, `SessionContextLoader`, `PromptComposer`, and an in-memory chat-history store.
Same shape as `AgentExecutorIntegrationTests`, but with a real `IChatClient` (supplied by the caller)
rather than the scripted one. A real DB (not mocks) is used so `CreateCharacter`/`ConfigureCampaign`/
`StartCampaign` actually persist and the model proceeds through the chain like production, and so
turn 1's state/history flows into turn 2.

`EvalTurnRunner.RunTurnAsync(playerMessage)` reproduces what `TurnCoordinator` does per turn, minus
the SSE/transaction machinery (irrelevant to tool order):

1. `SessionContextLoader.LoadAsync` → `SessionContext`; `DeriveStage()` (CharacterCreation).
2. `AgentToolProvider.GetToolsForStage(context, stage)`.
3. `agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct)`, collecting every
   `ToolResult` event in order → `IReadOnlyList<string>` of tool names. Persist the user + assistant
   messages to the in-memory history so the next turn sees this exchange.

It returns the ordered tool-call names. The code comment notes it intentionally mirrors
`TurnCoordinator`'s load/persist steps so the duplication does not silently drift.

**Caching wiring (the crux):** `AgentExecutor` takes an `IChatClient`. The harness is given the
**cached** client the reporting layer exposes (`scenarioRun.ChatConfiguration.ChatClient`) rather than
the raw Azure client, so every model call the agent makes is recorded on first run and replayed
thereafter.

### Custom evaluator

`ExpectedToolCallOrderContext : EvaluationContext` carries the expected ordered tool names into the
evaluator per scenario run (the idiomatic way expectations reach an `IEvaluator`). E.g.
`new ExpectedToolCallOrderContext(["CreateCharacter", "ConfigureCampaign", "StartCampaign"])`, or `[]`
for the "begin" turn.

`ToolCallOrderEvaluator : IEvaluator`:

- `EvaluationMetricNames => ["Tool Call Order"]`.
- `EvaluateAsync(messages, modelResponse, chatConfiguration, additionalContext, ct)`:
  - **Actual** = `FunctionCallContent` names from `modelResponse`, in order. The harness packages the
    captured tool-call order onto `modelResponse` as an assistant message whose `Contents` are
    `FunctionCallContent(name)` items, so the evaluator stays a generic "tool-call order over a
    `ChatResponse`" check, decoupled from `GameTurnEvent`.
  - **Expected** = from the `ExpectedToolCallOrderContext` in `additionalContext`.
  - Comparison = **exact ordered equality** (`actual.SequenceEqual(expected)`). Returns
    `BooleanMetric("Tool Call Order", passed)` with an `Interpretation` marking pass/fail and
    `Diagnostics` listing `expected: […]` vs `actual: […]`.

Exact match (not "subsequence with extras allowed") is deliberate: campaign creation should call
exactly those three tools, and a stray call — or any tool on the "begin" turn — is itself a finding.
A mode flag to relax this can be added later if a scenario needs it.

### Scenarios, reporting & caching

`CampaignCreationEvals`:

- Build a `ChatConfiguration` from the real Azure `IChatClient` (reusing `AgentConfiguration`). Create
  one disk-based `ReportingConfiguration` with response caching enabled, the `ToolCallOrderEvaluator`,
  and the `.eval-results/` storage path.
- Two `[Fact]` scenarios sharing one `EvalHost` (so turn 1 state/history flows into turn 2):
  - `Turn1_Begin_CallsNoTools` — scenario "CampaignCreation/Turn1-Begin"; `RunTurnAsync("begin")`;
    evaluate with `ExpectedToolCallOrderContext([])`.
  - `Turn2_Name_CreatesCampaignInOrder` — scenario "CampaignCreation/Turn2-Name";
    `RunTurnAsync("Grim")`; evaluate with
    `ExpectedToolCallOrderContext(["CreateCharacter","ConfigureCampaign","StartCampaign"])`.
- Each scenario: `await using var run = await reportingConfig.CreateScenarioRunAsync(name)`; build the
  agent on `run.ChatConfiguration.ChatClient`; run the turn; package the captured order onto a
  `ChatResponse`; `await run.EvaluateAsync(messages, response, additionalContext: [expected])`.
  Disposal persists the result. The test also asserts the metric passed, so a regression fails the run.

**Creds gating:** each `[Fact]` checks for the Azure env vars (endpoint/key/deployment); absent →
`Assert.Skip`. First run with creds records the cache; later runs replay without creds. CI (no creds)
skips cleanly.

**Viewing results:** results land in `.eval-results/`; `dotnet aieval report` (the
`Microsoft.Extensions.AI.Evaluation.Console` tool) renders an HTML report. The project README documents
the exact command and required env vars.

### Error handling

- A turn that throws (model/transport error) fails the scenario loudly with the exception — no silent
  pass.
- A turn that returns the wrong/empty tool calls is a normal metric **failure** with diagnostics, not
  an error.

## Out of scope (YAGNI)

- Quality/safety/NLP evaluators (narration coherence, groundedness) — a later eval, not this one.
- Combat / exploration / resolution scenarios — this eval is campaign-creation only.
- Committing the response cache as a CI artifact — the cache is local; CI skips without creds.
- Reusing `TurnCoordinator` or the HTTP loop — deliberately driving `AgentExecutor` directly.

## Verification

- `dotnet build` of the new project succeeds; it is excluded from the default `dotnet test` CI path.
- With Azure creds: both scenarios run, first pass populates `.eval-results/` cache, the metrics are
  asserted, and `dotnet aieval report` renders a report showing both scenarios.
- Without creds: both `[Fact]`s skip cleanly.
- A deliberately-wrong expected order (sanity check during implementation) produces a failing
  `BooleanMetric` with legible `expected` vs `actual` diagnostics.
