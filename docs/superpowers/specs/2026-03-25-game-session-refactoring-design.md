# GameSessionService Refactoring — Design Spec

**Date:** 2026-03-25
**Status:** Approved
**Scope:** Decompose GameSessionService, native SSE, structured observability, stage-scoped kernels

## Context

GameSessionService is 488 lines with 10 distinct responsibilities: state loading, kernel building, agent creation, streaming, transaction management, SSE emission, state serialization, combat delegation, chat history persistence, and error handling. It has zero logging. CombatAgentService duplicates the streaming/tool-result pattern. The stage machine has a runaway bug where the model chains through all 6 stages in one turn — undiagnosable without a debugger because there's no observability.

## Goals

1. Decompose GameSessionService into focused, testable services
2. Replace manual SSE with .NET 10 native `Results.ServerSentEvents`
3. Add structured logging + custom OTel traces for full turn visibility
4. Fix the stage runaway bug by only registering stage-appropriate plugins on the kernel

## Non-Goals

- Frontend changes (SSE contract stays identical)
- Domain model changes (Campaign, Character, Encounter untouched)
- Wrapper plugin changes (CharacterWrapperPlugin etc. stay as-is)
- New gameplay features

---

## 1. Service Decomposition

### 1.1 New Services

| Service | Responsibility | Est. Lines |
|---------|---------------|------------|
| `TurnCoordinator` | Orchestrates turn sequence. No business logic. Owns the transaction. | ~60 |
| `SessionContextLoader` | Loads campaign, character, encounter from repos into `SessionContext` | ~50 |
| `KernelFactory` | Builds Kernel with only stage-appropriate plugins | ~80 |
| `AgentExecutor` | Creates ChatCompletionAgent, streams response, extracts tool results | ~80 |
| `StateUpdateMapper` | Maps post-turn domain state to SSE state_update payload. Pure function. | ~60 |

### 1.2 TurnCoordinator Flow

```
LoadContext → DeriveStage → BuildKernel(stage) → ExecuteAgent → PersistState → MapStateUpdate → yield events
```

- Each step is a method call on an injected service
- Transaction wrapping (begin/commit/rollback) stays in the coordinator
- Combat delegation: when stage is Combat, coordinator calls CombatAgentService instead of AgentExecutor

### 1.3 CombatAgentService

Remains a separate service but reuses `AgentExecutor` for the streaming/tool-result extraction pattern instead of duplicating it. Handles combat-specific logic: iteration loop, encounter end detection, narrative accumulation.

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

`KernelFactory` receives the derived `SessionStage` and `SessionContext`, builds a kernel with only the plugins that stage needs. The model physically cannot call functions from other stages.

### 2.2 Stage-to-Plugin Mapping

| Stage | Plugins Registered |
|-------|-------------------|
| CharacterCreation | Character |
| CampaignSetup | Campaign |
| Exploration | Character, Campaign, Encounter, Dice |
| Combat | Encounter, Dice |
| Resolution | Character, Campaign, Resolution |
| Ended | (none — narration only) |

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
return Results.ServerSentEvents(turnCoordinator.ExecuteTurn(...));
```

### 3.2 Event Model

Delete `SseEvent` record. Replace with typed hierarchy:

```csharp
public abstract record GameTurnEvent;
public record NarrativeChunk(string Text) : GameTurnEvent;
public record ToolResult(string Function, object Result) : GameTurnEvent;
public record StateUpdate(...fields...) : GameTurnEvent;
public record TurnError(string Message) : GameTurnEvent;
```

Each variant maps to an SSE event type via `SseItem.Create(data, eventType: "narrative")`.

### 3.3 Channel Bridge Eliminated

TurnCoordinator returns `IAsyncEnumerable<SseItem<GameTurnEvent>>` directly using `yield return`. No fire-and-forget task, no Channel. The async enumerable handles backpressure naturally.

### 3.4 Frontend Impact

Zero. Same event types (`narrative`, `tool_result`, `state_update`, `error`), same JSON shapes. The `done` event is removed — stream close signals completion. Frontend already handles stream close.

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
| `KernelFactory` | Each stage registers exactly the right plugins | Build kernel, assert plugin/function names |
| `AgentExecutor` | Streams chunks, extracts tool results, handles cancellation | Mock IChatCompletionService |
| `StateUpdateMapper` | Correct field mapping from domain to event | Pure function, assert output |
| `TurnCoordinator` | Service call order, transaction commit/rollback | Mock all services, verify sequence |

### 5.2 Key Regression Test

`KernelFactory` test per stage verifying the exact function list. This is the hard gate replacing StageTransitionFilter — if a plugin is added to the wrong stage, the test fails.

### 5.3 Deleted Tests

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
- Tests for each new service

### Modified Files
- `Endpoints/SessionEndpoints.cs` — native SSE, call TurnCoordinator
- `Configuration/SemanticKernelConfiguration.cs` — register new services
- `Configuration/OpenTelemetryConfiguration.cs` — add custom ActivitySource
- `Plugins/CombatAgent/CombatAgentService.cs` — reuse AgentExecutor, add to DI

### Deleted Files
- `Services/GameSessionService.cs` — replaced by TurnCoordinator + services
- `Services/StageTransitionFilter.cs` — replaced by kernel scoping
- `Services/StagePluginRegistry.cs` — replaced by KernelFactory
- `Models/SseEvent.cs` — replaced by GameTurnEvent
- `Tests/StateMachine/StageTransitionTests.cs` — filter deleted
- `Tests/StateMachine/StagePluginRegistryTests.cs` — registry deleted
