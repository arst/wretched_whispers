# Engine Extraction: Shared AI/Game Layer for Any Client

**Date:** 2026-07-20
**Status:** Approved
**Goal:** Move the AI game-master layer out of the ASP.NET Api project into a new
`WretchedWhispers.Engine` class library so a future native client (Avalonia/MAUI/console)
can run the game in-process without hosting an HTTP server.

## Problem

The entire LLM/agent layer — `TurnCoordinator`, `AgentExecutor`, `GameTools/`, `Prompts/`,
prompt composition, chat-history reduction, LLM client configuration — lives inside
`WretchedWhispers.Api`, the ASP.NET web project. The desktop app therefore ships a whole
web server (Photino wrapping localhost) just to reach game logic. A native client cannot
reference the game engine without dragging in HTTP hosting.

Verified: none of the files in `Services/`, `GameTools/`, `Prompts/`, or `GameTurnEvent`
reference ASP.NET types. The layer is already logically decoupled; it just lives in the
wrong csproj. The refactor is mechanical: move files, rename namespaces, split package refs.

## Decision

**Approach A** (chosen over "merge into Infrastructure" and "full Contracts + TS SDK split"):
one new class library between Infrastructure and Api.

Dependency chain: `Core ← Infrastructure ← Engine ← Api`.

## New project: `WretchedWhispers.Engine`

- net10.0 class library
- References: `WretchedWhispers.Infrastructure` (project)
- Package refs taken over from Api: `Azure.AI.OpenAI`, `OpenAI`, `Microsoft.Agents.AI`,
  `Microsoft.Agents.AI.OpenAI` (+ whatever Microsoft.Extensions.* abstractions the compiler
  demands that don't flow transitively)

## What moves (git mv + namespace `WretchedWhispers.Api.*` → `WretchedWhispers.Engine.*`)

| From (Api) | Contents |
|---|---|
| `Services/` (all files) | TurnCoordinator, AgentExecutor + interface, AgentToolProvider + interface, GameToolCatalog, PromptComposer, ChatHistoryReducer, SessionContextLoader/Context/Stage + interface, OutputScrubber, StateUpdateMapper, TurnDeltaMapper, SessionConcurrencyGuard, TraceExporter |
| `GameTools/` (all files) | CampaignTools, CharacterTools, DiceTools, EncounterTools, GameToolAttribute, ToolGuard, `GameTools/Models/` DTOs |
| `Prompts/` | NarratorPersona, StagePrompts |
| `Models/GameTurnEvent.cs` | The engine's output contract: NarrativeChunk, ToolResult, TurnDelta, StateUpdate, TurnError, TurnDone |
| `Models/AzureOpenAiSettings.cs` | Bound by AddGameAgent |
| `Configuration/AgentConfiguration.cs` | `AddGameAgent(IServiceCollection, IConfiguration)` — stays the single DI entry point, now exported by Engine |
| `Configuration/DesktopLlmOptions.cs` | Incl. ReloadableOpenAIChatClient |

If a wire DTO turns out to be referenced by moved code (e.g. `ChatMessageDto`), it moves
too — decided by the compiler during implementation, not upfront.

## What stays in Api (pure HTTP/hosting)

Endpoints (SSE serialization of `GameTurnEvent`), Auth (`LocalAuthHandler`), `Program.cs`,
Photino `Desktop/DesktopHost`, `SettingsEndpoints`, `OpenTelemetryConfiguration` (+ OTel
packages incl. `Instrumentation.AspNetCore`), wire-only DTOs (`ChatMessageDto`,
`CreateSession*`, `PlayerActionRequest`, `SessionDetailDto`, `SessionPreviewDto`), wwwroot.

## Engine public surface for a native client

- `services.AddGameAgent(configuration)` + existing Infrastructure DI → fully wired engine
- `TurnCoordinator.ExecuteTurnAsync(sessionId, message, ct)` →
  `IAsyncEnumerable<GameTurnEvent>` — native clients consume the records directly (no SSE)
- Session/campaign CRUD via existing Core services + Infrastructure repositories (same path
  the Api uses)

No new abstractions, no renamed classes, no behavior change.

## Ripples

- `WretchedWhispers.Evals` re-points its Api reference to Engine (cleaner fit — it
  exercises prompts/tools, not endpoints).
- `WretchedWhispers.Tests` adds an Engine reference; keeps the Api reference only if
  endpoint-level tests need it (decided by what the tests actually import).
- Stale `WretchedWhispers.Semantic` ProjectReference in Tests.csproj gets deleted
  (leftover from the SK removal).
- All `using WretchedWhispers.Api.{Services,GameTools,Prompts,Models}` updated mechanically.

## Baseline fixes (step 0 — already applied)

Main did not build: floating `9.0.*` versions of the ASP.NET/EF packages resolved to
9.0.18 in Infrastructure but 9.0.17 in Tests/Evals (stale restore) → CS1705. Fixed by
`dotnet restore --force-evaluate` and pinning the floating versions to 9.0.18 so partial
restores can't drift again. 409 tests green.

## Testing

Pure refactor — the existing suite is the safety net. No new tests. Verification:

1. `dotnet build WrtechedWhispers.sln` — zero errors
2. `dotnet test WretchedWhispers.Tests` — all 409 pass
3. `./build-desktop.sh` — desktop app still builds and runs

## Out of scope

Renaming classes, a separate Contracts package, TypeScript client SDK, any change to the
Next.js frontend, actually building a native client.
