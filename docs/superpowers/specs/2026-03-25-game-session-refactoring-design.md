# GameSessionService Refactoring — Design Spec

**Date:** 2026-03-25
**Status:** Draft
**Scope:** Decompose GameSessionService, native SSE, structured observability, stage-scoped kernels

## Context

GameSessionService is 488 lines with 10 distinct responsibilities: state loading, kernel building, agent creation, streaming, transaction management, SSE emission, state serialization, combat delegation, chat history persistence, and error handling. It has zero logging. CombatAgentService duplicates the streaming/tool-result pattern. The stage machine has a runaway bug where the model chains through all 6 stages in one turn — undiagnosable without a debugger because there's no observability.

## Goals

1. Decompose GameSessionService into focused, testable services
2. Replace manual SSE with .NET 10 native `Results.ServerSentEvents`
3. Add structured logging + custom OTel traces for full turn visibility
4. Fix the stage runaway bug by only registering stage-appropriate functions on the kernel

## Non-Goals

- Domain model changes (Campaign, Character, Encounter untouched)
- Wrapper plugin changes (CharacterWrapperPlugin etc. stay as-is)
- New gameplay features

---

## 1. Service Decomposition

### 1.1 New Services

| Service | Responsibility | Dependencies | Est. Lines |
|---------|---------------|-------------|------------|
| `TurnCoordinator` | Orchestrates turn sequence. No business logic. Owns the transaction. Persists chat messages (user input before agent, assistant response after). | All services below, `IChatHistoryRepository`, `WretchedWhispersDbContext` | ~80 |
| `SessionContextLoader` | Loads campaign, character, encounter from repos into `SessionContext` | `ICampaignsRepository`, `ICharactersRepository`, `IEncountersRepository` | ~50 |
| `KernelFactory` | Builds Kernel with only stage-appropriate functions. Receives Azure OpenAI settings via `IOptions<AzureOpenAiSettings>`. Resolves original plugins from `IServiceProvider`, wraps them, imports only the functions allowed for the stage. | `IServiceProvider`, `IOptions<AzureOpenAiSettings>` | ~100 |
| `AgentExecutor` | Loads chat history, configures `ChatHistorySummarizationReducer`, creates `ChatCompletionAgent` with `PromptComposer` instructions, streams response, extracts tool results. Wraps invocation in resilience pipeline. | `IChatHistoryRepository`, `PromptComposer`, `ResiliencePipelineProvider<string>` | ~100 |
| `StateUpdateMapper` | Maps post-turn domain state to `StateUpdate` event payload. Pure function, no dependencies. Also used by `GetSessionDetail` endpoint to eliminate the duplicated mapping block in `SessionEndpoints.cs`. | None | ~60 |

### 1.2 TurnCoordinator Flow

```
SaveUserMessage → LoadContext → DeriveStage → BuildKernel(stage) → ExecuteAgent → SaveAssistantResponse → ReloadContext → MapStateUpdate → yield events
```

- Each step is a method call on an injected service
- Transaction wrapping (begin/commit/rollback) stays in the coordinator
- Combat delegation: when stage is Combat, coordinator calls CombatAgentService (which reuses AgentExecutor internally) instead of calling AgentExecutor directly
- Chat history persistence: coordinator saves user message before agent runs and assistant response after (both within the transaction)
- `SessionConcurrencyGuard` remains in the endpoint (acquire before coordinator, release via `WithGuardRelease` wrapper on the async enumerable — cannot use try/finally around `Results.ServerSentEvents` because it returns before stream is consumed)

### 1.3 CombatAgentService

Remains a separate service with its own streaming loop. Combat has a fundamentally different execution pattern (multi-turn iteration with break conditions on encounter end / character death) that doesn't map to AgentExecutor's single-invocation model. The streaming/tool-result extraction is similar code but the control flow differs enough that forcing delegation would add complexity without reducing duplication. Registered in DI (currently instantiated with `new`).

### 1.4 Deleted Classes

- `StageTransitionFilter` — replaced by physical kernel scoping
- `StagePluginRegistry` — replaced by KernelFactory stage mapping
- `SseEvent` record — replaced by `GameTurnEvent` hierarchy

### 1.5 DI Registration

All new services registered as Scoped (they depend on scoped DbContext and repositories):
- `TurnCoordinator`
- `SessionContextLoader`
- `KernelFactory`
- `AgentExecutor`
- `StateUpdateMapper`
- `CombatAgentService` (currently instantiated with `new`, moved to DI)

---

## 2. Stage-Scoped Kernel

### 2.1 Approach

`KernelFactory` receives the derived `SessionStage` and `SessionContext`, builds a kernel with only the functions that stage needs. The model physically cannot call functions from other stages. Wrapper plugins are constructed by resolving original plugins from `IServiceProvider` and wrapping them with adapters (same pattern as current `BuildKernelForSession`, but only importing the functions the stage allows).

### 2.2 Stage-to-Function Mapping

Function-level granularity (not plugin-level). Only the specific functions needed for each stage are registered:

| Stage | Plugin | Functions |
|-------|--------|-----------|
| CharacterCreation | Character | `CreateCharacter` |
| CampaignSetup | Campaign | `ConfigureCampaign`, `StartCampaign` |
| Exploration | Character | `ChallengeCharacter`, `AddItemToCharacterInventory`, `BuyItem`, `CastScroll` |
| | Campaign | `AdvanceTime`, `Rest` |
| | Encounter | `CreateEncounter`, `AddAdversaryToEncounter`, `StartEncounter` |
| | Dice | `Roll` |
| Combat | Encounter | `AttackPlayer`, `AttackAdversary`, `EndEncounter` |
| | Dice | `Roll` |
| Resolution | Character | `AddItemToCharacterInventory`, `RemoveItemFromCharacterInventory`, `InfectCharacter`, `CureInfection`, `ImproveCharacterAbility`, `DegradeCharacterAbility` |
| | Campaign | `AdvanceTime` |
| | Resolution | `CompleteResolution` |
| Ended | (none) | (narration only, no tools) |

Implementation: `KernelFactory` imports the full wrapper plugin, then uses `kernel.Plugins` to programmatically remove functions not in the stage's allowed set — or selectively imports only the needed functions using `KernelPluginFactory.CreateFromFunctions()`.

### 2.3 Implications

- No `StageTransitionFilter` needed — constraint is physical
- No `StagePluginRegistry` needed — factory handles mapping directly
- `FunctionChoiceBehavior.Auto()` with no arguments — every function on the kernel is allowed
- Wrapper plugins unchanged — still auto-fill IDs and enforce guardrails
- Stage transitions happen between turns only (kernel rebuilt on next turn from fresh domain state)

---

## 3. Native SSE

### 3.1 Endpoint Change

Replace manual SSE formatting with .NET 10 `Results.ServerSentEvents<T>()`:

```csharp
// Before (manual)
http.Response.ContentType = "text/event-stream";
await foreach (var sseEvent in gameService.ProcessAction(...)) { ... }

// After (native)
return Results.ServerSentEvents(
    turnCoordinator.ExecuteTurn(sessionId, request.Message, ct)
        .Select(evt => SseItem.Create(evt, eventType: evt.EventType)));
```

`SessionConcurrencyGuard` is acquired before calling the coordinator and released via a `WithGuardRelease` async enumerable wrapper (because `Results.ServerSentEvents` returns before the stream is consumed, a try/finally around the call would release immediately).

### 3.2 Event Model

Delete `SseEvent` record. Replace with typed hierarchy:

```csharp
public abstract record GameTurnEvent(string EventType);
public record NarrativeChunk(string Text) : GameTurnEvent("narrative");
public record ToolResult(string Function, object Result) : GameTurnEvent("tool_result");
public record StateUpdate(...fields...) : GameTurnEvent("state_update");
public record TurnError(string Message) : GameTurnEvent("error");
public record TurnDone() : GameTurnEvent("done");
```

### 3.3 Channel Bridge Eliminated

`TurnCoordinator` returns `IAsyncEnumerable<GameTurnEvent>` using `yield return`. The endpoint maps each event to `SseItem<GameTurnEvent>` via `.Select()`. No fire-and-forget task, no Channel. The async enumerable handles backpressure naturally.

### 3.4 Cancellation

When the client disconnects, the `CancellationToken` passed to the async enumerable fires. `Results.ServerSentEvents` propagates this automatically. The coordinator catches `OperationCanceledException` and rolls back the transaction (same as current behavior).

### 3.5 Frontend Impact

Zero. Same event types (`narrative`, `tool_result`, `state_update`, `error`, `done`), same JSON shapes. The `done` event is kept as the last emitted event before the stream closes, preserving the current contract.

---

## 4. Observability

### 4.1 Structured Logging

Each decomposed service gets `ILogger<T>` via DI.

| Event | Level | Service | Key Properties |
|-------|-------|---------|---------------|
| Turn started | Info | TurnCoordinator | `SessionId`, `Stage`, `PluginsRegistered[]` |
| Function invoked | Info | AgentExecutor | `Plugin`, `Function`, `Duration` |
| Function result | Debug | AgentExecutor | `Plugin`, `Function`, `ResultSummary` |
| State mutation | Info | SessionContextLoader | `Field`, `Before`, `After` |
| Turn completed | Info | TurnCoordinator | `Stage`, `Duration`, `FunctionsCalled`, `NarrativeLength` |
| Turn failed | Error | TurnCoordinator | `Stage`, `Exception`, `FunctionsCalled` |
| Combat delegated | Info | TurnCoordinator | `EncounterId`, `AdversaryCount` |
| Combat iteration | Debug | CombatAgentService | `Iteration`, `AdversariesRemaining`, `CharacterHp` |

### 4.2 Custom OTel Traces

One `ActivitySource` named `WretchedWhispers.GameTurn`:

- **Turn span** — wraps entire turn. Tags: `session.id`, `session.stage`, `turn.duration`
- **Child spans:** `LoadContext`, `BuildKernel`, `ExecuteAgent`, `PersistState`, `MapStateUpdate`
- **Function call spans** — nested under ExecuteAgent. Tags: `function.name`, `function.plugin`, `function.duration`

Plugs into existing OTLP exporter configuration. Traces appear alongside Semantic Kernel's built-in spans.

### 4.3 Registration

Add `WretchedWhispers.GameTurn` to the existing OpenTelemetry tracing sources in `OpenTelemetryConfiguration.cs`.

---

## 5. Testing Strategy

### 5.1 Unit Tests

| Service | What to Test | Approach |
|---------|-------------|----------|
| `SessionContextLoader` | Stage derivation from various domain states | Mock repos, assert SessionContext + stage |
| `KernelFactory` | Each stage registers exactly the right functions (function-level) | Build kernel, assert exact function names per stage |
| `AgentExecutor` | Streams chunks, extracts tool results, handles cancellation, resilience retry | Mock IChatCompletionService, verify yielded events |
| `StateUpdateMapper` | Correct field mapping from domain to event | Pure function, assert output |
| `TurnCoordinator` | Service call order, transaction commit/rollback, chat persistence | Mock all services, verify sequence |

### 5.2 Key Regression Test

`KernelFactory` test per stage verifying the exact function list. This is the hard gate replacing StageTransitionFilter — if a function is added to the wrong stage, the test fails.

### 5.3 Integration Test

One SSE endpoint integration test that sends a message and verifies the response stream contains properly serialized SSE events (`event:`, `data:` lines). Uses `WebApplicationFactory<Program>` with test doubles for the LLM.

### 5.4 Deleted Tests

- `StageTransitionTests` (filter tests) — class deleted
- `StagePluginRegistryTests` — class deleted

Existing wrapper plugin tests and stage derivation tests remain unchanged.

---

## 6. File Changes Summary

### New Files
- `Services/TurnCoordinator.cs`
- `Services/SessionContextLoader.cs`
- `Services/KernelFactory.cs`
- `Services/AgentExecutor.cs`
- `Services/StateUpdateMapper.cs`
- `Models/GameTurnEvent.cs` (replaces SseEvent.cs)
- `Models/AzureOpenAiSettings.cs` (strongly-typed config)
- Tests for each new service + SSE integration test

### Modified Files
- `Endpoints/SessionEndpoints.cs` — native SSE, call TurnCoordinator, use StateUpdateMapper for GetSessionDetail
- `Configuration/SemanticKernelConfiguration.cs` — register new services, bind AzureOpenAiSettings
- `Configuration/OpenTelemetryConfiguration.cs` — add custom ActivitySource
- `Plugins/CombatAgent/CombatAgentService.cs` — reuse AgentExecutor, register in DI

### Deleted Files
- `Services/GameSessionService.cs` — replaced by TurnCoordinator + services
- `Services/StageTransitionFilter.cs` — replaced by kernel scoping
- `Services/StagePluginRegistry.cs` — replaced by KernelFactory
- `Models/SseEvent.cs` — replaced by GameTurnEvent
- `Tests/StateMachine/StageTransitionTests.cs` — filter deleted
- `Tests/StateMachine/StagePluginRegistryTests.cs` — registry deleted
