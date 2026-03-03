---
phase: 03-api-layer-and-streaming
plan: 02
subsystem: api
tags: [sse, streaming, semantic-kernel, resilience, concurrency-guard, game-session, minimal-api]

# Dependency graph
requires:
  - phase: 03-api-layer-and-streaming
    plan: 01
    provides: "Session CRUD endpoints, OTel, DTOs, endpoint group pattern, SK package references"
provides:
  - "POST /sessions/{id}/actions SSE streaming endpoint"
  - "GameSessionService per-turn agent orchestration with transactional DB commit"
  - "SessionConcurrencyGuard for 409 Conflict on double-submit"
  - "SseEvent typed model with narrative/tool_result/state_update/error/done events"
  - "SemanticKernelConfiguration with plugin DI and LLM resilience pipeline"
  - "9 integration + unit tests for streaming and concurrency"
affects: [04-frontend]

# Tech tracking
tech-stack:
  added: []
  patterns: [per-turn-kernel-build, scoped-plugin-import, transactional-agent-turn, manual-sse-streaming, resilience-pipeline-llm-retry]

key-files:
  created:
    - WrtechedWhispers/WretchedWhispers.Api/Services/GameSessionService.cs
    - WrtechedWhispers/WretchedWhispers.Api/Services/SessionConcurrencyGuard.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/SseEvent.cs
    - WrtechedWhispers/WretchedWhispers.Api/Models/PlayerActionRequest.cs
    - WrtechedWhispers/WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs
    - WrtechedWhispers/WretchedWhispers.Tests/Sessions/SessionStreamingTests.cs
  modified:
    - WrtechedWhispers/WretchedWhispers.Api/Endpoints/SessionEndpoints.cs
    - WrtechedWhispers/WretchedWhispers.Api/Program.cs

key-decisions:
  - "Plugins registered as Scoped services and resolved via ImportPluginFromObject to ensure request-scoped DbContext alignment"
  - "Events buffered in list then yielded (C# prohibits yield in try/catch blocks)"
  - "Transactional agent turn: BeginTransaction before agent invoke, CommitTransaction after success, RollbackTransaction on failure"

patterns-established:
  - "Per-turn Kernel build: GameSessionService creates fresh Kernel per ProcessAction call, imports scoped plugin instances"
  - "SSE event model: SseEvent record with EventType and JsonData, camelCase JSON serialization"
  - "Concurrency guard: ConcurrentDictionary<Guid, SemaphoreSlim> singleton, TryAcquire/Release pattern"
  - "Resilience pipeline: Polly v8 exponential backoff with jitter for LLM transient failures"

requirements-completed: [INFR-01, SESS-04, GAME-06]

# Metrics
duration: 7min
completed: 2026-03-03
---

# Phase 3 Plan 2: Streaming Action Endpoint Summary

**GameSessionService with per-turn SK agent orchestration, SSE streaming via manual Response.WriteAsync, transactional DB commit/rollback, and Polly resilience pipeline for LLM retry**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-03T14:27:17Z
- **Completed:** 2026-03-03T14:35:13Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments
- GameSessionService orchestrates complete agent turn: Kernel build, plugin import, streaming invocation, chat history persistence, transactional commit
- POST /sessions/{id}/actions endpoint streams typed SSE events (narrative, tool_result, state_update, error, done) with proper flush-after-write
- SessionConcurrencyGuard prevents double-submit with per-session SemaphoreSlim returning 409 Conflict
- Resilience pipeline retries transient LLM failures (HttpRequestException, TaskCanceledException) with exponential backoff and 180s timeout
- 9 new tests: 4 streaming endpoint integration tests + 5 concurrency guard unit tests, all 239 total pass

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SSE models, concurrency guard, SK configuration, and GameSessionService** - `ad944b1` (feat)
2. **Task 2: Wire POST /sessions/{id}/actions SSE endpoint and update Program.cs** - `f092c3b` (feat)
3. **Task 3: Integration tests for streaming action endpoint and error handling** - `eaf948c` (test)

## Files Created/Modified
- `WretchedWhispers.Api/Services/GameSessionService.cs` - Per-turn agent orchestration: Kernel creation, plugin import, streaming, transactional commit
- `WretchedWhispers.Api/Services/SessionConcurrencyGuard.cs` - Per-session ConcurrentDictionary<Guid, SemaphoreSlim> for 409 Conflict
- `WretchedWhispers.Api/Models/SseEvent.cs` - Typed SSE event record with EventType and camelCase JsonData serialization
- `WretchedWhispers.Api/Models/PlayerActionRequest.cs` - Request body record for POST action
- `WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs` - SK DI registration: plugins as Scoped, GameSessionService, resilience pipeline
- `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` - Added POST /{sessionId}/actions with SSE streaming and concurrency guard
- `WretchedWhispers.Api/Program.cs` - Wired AddSemanticKernel DI configuration
- `WretchedWhispers.Tests/Sessions/SessionStreamingTests.cs` - 4 integration tests + 5 concurrency guard unit tests

## Decisions Made
- Plugins registered as Scoped services and imported via `ImportPluginFromObject()` to ensure request-scoped DbContext alignment (avoiding ImportPluginFromType's root-provider resolution)
- Events buffered into a List<SseEvent> then yielded after try/catch completes -- C# does not allow `yield return` inside try/catch blocks
- Database transaction wraps the entire agent turn: individual plugin SaveChanges calls are captured in the transaction, committed only after full agent response success
- GM agent definition (instructions, summarization reducer, function choice behavior) copied exactly from SingleAgent.Console/Program.cs to maintain consistency

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed AgentResponseItem streaming API access**
- **Found during:** Task 1
- **Issue:** Plan specified `chunk.Content` for streaming but SK 1.65.0 returns `AgentResponseItem<StreamingChatMessageContent>` which requires `.Message.Content`
- **Fix:** Used `response.Message.Content` to access the streaming text content
- **Files modified:** GameSessionService.cs
- **Committed in:** ad944b1 (Task 1 commit)

**2. [Rule 3 - Blocking] Fixed yield-in-try/catch C# limitation**
- **Found during:** Task 1
- **Issue:** C# does not allow `yield return` inside try/catch blocks (CS1626/CS1631 compiler errors)
- **Fix:** Restructured ProcessAction to delegate to ExecuteAgentTurn which returns a List<SseEvent>, then yield each event from the list outside try/catch
- **Files modified:** GameSessionService.cs
- **Committed in:** ad944b1 (Task 1 commit)

**3. [Rule 3 - Blocking] Fixed ResiliencePipelineProvider namespace**
- **Found during:** Task 1
- **Issue:** `ResiliencePipelineProvider<string>` is in `Polly.Registry` namespace, not `Polly` or `Microsoft.Extensions.Resilience`
- **Fix:** Changed using from `Microsoft.Extensions.Resilience` to `Polly.Registry`
- **Files modified:** GameSessionService.cs
- **Committed in:** ad944b1 (Task 1 commit)

---

**Total deviations:** 3 auto-fixed (1 bug, 2 blocking)
**Impact on plan:** All auto-fixes necessary for correctness. No scope creep. The buffered events approach means narrative chunks are not streamed token-by-token to the client during the agent turn (they are collected and sent after the resilience pipeline completes), but this maintains the transactional guarantee. True per-token SSE streaming can be added in a future optimization when the resilience pipeline is restructured.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None -- AzureOpenAI settings remain as placeholders in appsettings.json. To test with a real LLM, configure via user secrets:
```bash
cd WrtechedWhispers/WretchedWhispers.Api
dotnet user-secrets set "AzureOpenAi:ChatModelDeployment" "your-deployment"
dotnet user-secrets set "AzureOpenAi:Endpoint" "https://your-endpoint.openai.azure.com/"
dotnet user-secrets set "AzureOpenAi:ApiKey" "your-key"
```

## Next Phase Readiness
- Full API layer complete: session CRUD + streaming action endpoint with SSE
- All patterns established for frontend integration: SSE event types, content-type negotiation, error handling
- 239 total tests pass (230 existing + 9 new)
- Ready for Phase 4: Frontend integration

---
*Phase: 03-api-layer-and-streaming*
*Completed: 2026-03-03*

## Self-Check: PASSED
- All 8 files verified on disk
- All 3 task commits verified in git history (ad944b1, f092c3b, eaf948c)
