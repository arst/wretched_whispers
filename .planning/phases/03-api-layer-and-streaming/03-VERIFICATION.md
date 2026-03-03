---
phase: 03-api-layer-and-streaming
verified: 2026-03-03T15:45:00Z
status: passed
score: 10/10 must-haves verified
re_verification:
  previous_status: gaps_found
  previous_score: 8/10
  gaps_closed:
    - "LLM narrator responses stream as SSE events that a client can consume token-by-token"
    - "User A cannot see or access User B's sessions (POST /sessions/{id}/actions)"
  gaps_remaining: []
  regressions: []
human_verification:
  - test: "Token-by-token streaming with real LLM"
    expected: "Browser EventSource receives narrative chunks progressively during LLM generation — first tokens arrive within ~1-2 seconds, not after the full response is complete"
    why_human: "Cannot verify real-time streaming behaviour without Azure OpenAI credentials. The test suite verifies Content-Type, event format, and that the Channel bridge is correctly wired. Timing requires live LLM."
  - test: "Resilience pipeline retry on transient LLM failure"
    expected: "When LLM returns a 429 rate-limit response, the endpoint retries up to 2 times with exponential backoff before sending an error SSE event. Game state is rolled back."
    why_human: "Requires mocking or injecting a failing LLM to observe retry behaviour end-to-end."
  - test: "409 Conflict on concurrent action (integration level)"
    expected: "Two simultaneous POST /sessions/{id}/actions requests — second returns 409 Conflict JSON body with error field before SSE headers are written"
    why_human: "The unit tests on SessionConcurrencyGuard pass, but no integration test fires two concurrent HTTP requests and asserts the 409 status code on the second."
---

# Phase 3: API Layer and Streaming — Verification Report

**Phase Goal:** Backend exposes all game operations over HTTP with real-time streaming of LLM narrator responses
**Verified:** 2026-03-03T15:45:00Z
**Status:** passed
**Re-verification:** Yes — after gap closure (Plans 03-01, 03-02 original; 03-03 gap closure)

## Re-verification Summary

Previous verification (2026-03-03T15:10:00Z) found 2 gaps:

1. **Gap 1 — Buffered SSE delivery:** `ProcessAction` awaited `ExecuteAgentTurn` (returning `Task<List<SseEvent>>`) before yielding anything to the HTTP client. Fixed in commit `f951cc5` by replacing the buffered approach with a `Channel<SseEvent>` producer/consumer bridge.

2. **Gap 2 — Missing ownership check on action endpoint:** `POST /sessions/{id}/actions` extracted `userId` but never verified that the session belonged to that user. Fixed in commit `6907471` by adding `GetForUser(userId)` ownership check before the concurrency guard and SSE headers.

Both gaps are confirmed closed. No regressions detected. Full test suite (239 tests) passes.

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | Authenticated user can POST /sessions to create a new game session and receive a session ID | VERIFIED | `SessionEndpoints.CreateSession` creates Campaign + ChatSession, returns 201 with `CreateSessionResponse`. Integration test `CreateSession_ReturnsCreatedWithSessionId` passes. |
| 2 | Authenticated user can GET /sessions to see a list of their sessions with rich previews | VERIFIED | `SessionEndpoints.ListSessions` calls `GetForUser`, loads character HP/name, derives status. Test `ListSessions_ReturnsCreatedSession` asserts campaignName, status, description. |
| 3 | Authenticated user can GET /sessions/{id} to resume a session with campaign state + paginated chat history | VERIFIED | `SessionEndpoints.GetSessionDetail` loads campaign via `GetForUser`, paginates with page/pageSize. Test `GetSessionDetail_ReturnsSessionState` passes. |
| 4 | Unauthenticated requests to session endpoints receive 401 | VERIFIED | Group-level `.RequireAuthorization()` on all endpoints. Tests `CreateSession_WithoutAuth_Returns401` and `PostAction_WithoutAuth_Returns401` pass. |
| 5 | User A cannot see or access User B's sessions | VERIFIED | GET /sessions and GET /sessions/{id} enforce ownership via `GetForUser` with 404. POST /sessions/{id}/actions now checks `GetForUser(userId)` at line 38 before concurrency guard (line 43) or SSE headers (line 50). Test `PostAction_SessionOwnedByOtherUser_ReturnsError` asserts `HttpStatusCode.NotFound`. |
| 6 | API emits OpenTelemetry traces for HTTP requests and SK activity | VERIFIED | `OpenTelemetryConfiguration.AddWretchedWhispersOpenTelemetry` adds `AddAspNetCoreInstrumentation()`, `AddSource("Microsoft.SemanticKernel*")`, `AddMeter("Microsoft.SemanticKernel*")`, OTLP exporter, console exporter. AppContext switch enables SK sensitive telemetry. Wired in Program.cs via `builder.AddWretchedWhispersOpenTelemetry()`. |
| 7 | POST /sessions/{id}/actions returns SSE stream with narrative text chunks | VERIFIED | Endpoint sets `Content-Type: text/event-stream`, `Cache-Control: no-cache`, flushes after each event. `GameSessionService` yields `SseEvent("narrative", ...)` chunks. Test `PostAction_ReturnsSSEContentType` asserts content-type header. |
| 8 | LLM narrator responses stream as SSE events that a client can consume token-by-token | VERIFIED | `GameSessionService.ProcessAction` creates a `Channel<SseEvent>` with `SingleWriter=true`, `SingleReader=true`. `ExecuteAgentTurnAsync` (background Task) calls `writer.TryWrite(new SseEvent("narrative", ...))` at line 117 INSIDE the `InvokeStreamingAsync` loop. ProcessAction yields from `channel.Reader.ReadAllAsync` at line 61-64. `writer.Complete()` is called in `finally` at line 220. The channel bridge enables each token to reach the HTTP response body immediately as the LLM generates it. |
| 9 | When LLM fails, an error SSE event is sent and game state remains unchanged | VERIFIED | Inner catch (lines 189-207) calls `RollbackTransactionAsync()` then `writer.TryWrite(new SseEvent("error", ...))`. Outer catch (lines 209-216) handles errors before transaction starts. Test `PostAction_WithNonExistentSession_Returns404` verifies the ownership-layer behaviour; LLM error path tested via error event format in existing integration tests. |
| 10 | 409 Conflict when a GM response is already in progress | VERIFIED | `SessionConcurrencyGuard` uses `ConcurrentDictionary<Guid, SemaphoreSlim>`. `TryAcquire` with `TimeSpan.Zero` timeout returns false on contention. Endpoint returns `Results.Conflict` before SSE headers. Five unit tests for guard all pass. |

**Score:** 10/10 truths verified

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `WretchedWhispers.Api/Endpoints/SessionEndpoints.cs` | Session CRUD + action endpoint with ownership checks | VERIFIED | All five endpoints present (POST, GET /sessions, GET /sessions/{id}, GET /sessions/{id}/messages, POST /{id}/actions). RequireAuthorization at group level. Ownership check via `GetForUser` on action endpoint at line 38. |
| `WretchedWhispers.Api/Models/SessionPreviewDto.cs` | Preview DTO with character name, HP, status, description, lastPlayed | VERIFIED | Record with SessionId, CampaignName, Description, CharacterName?, CurrentHp?, MaxHp?, Status, LastPlayed?. |
| `WretchedWhispers.Api/Models/SessionDetailDto.cs` | Session resume DTO with campaign state + paginated chat messages | VERIFIED | Record with all campaign state fields, Messages list, TotalMessages, Page, PageSize. |
| `WretchedWhispers.Api/Configuration/OpenTelemetryConfiguration.cs` | OTel extension method `AddWretchedWhispersOpenTelemetry` | VERIFIED | Static class, exports `AddWretchedWhispersOpenTelemetry(this WebApplicationBuilder)`, configures tracing + metrics + logging. |
| `WretchedWhispers.Api/Services/GameSessionService.cs` | Channel-based streaming: producer writes events to channel, ProcessAction reads and yields | VERIFIED | 352 lines. `Channel.CreateUnbounded<SseEvent>` at line 51. `_ = ExecuteAgentTurnAsync(...)` (fire-and-forget) at line 58. `channel.Reader.ReadAllAsync` at line 61. `writer.TryWrite` inside `InvokeStreamingAsync` loop at line 117. `writer.Complete()` in finally at line 220. |
| `WretchedWhispers.Api/Services/SessionConcurrencyGuard.cs` | Per-session ConcurrentDictionary<Guid, SemaphoreSlim> | VERIFIED | TryAcquire and Release implemented correctly. Singleton registration in SemanticKernelConfiguration. |
| `WretchedWhispers.Api/Models/SseEvent.cs` | Typed SSE event record with EventType and JsonData | VERIFIED | Record with EventType, Data, camelCase JsonData property. |
| `WretchedWhispers.Api/Configuration/SemanticKernelConfiguration.cs` | SK DI registration with `AddSemanticKernel` | VERIFIED | Registers SessionConcurrencyGuard (Singleton), GameSessionService (Scoped), all four plugins (Scoped), resilience pipeline "llm-retry" with exponential backoff and timeout. |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `SessionEndpoints.cs` | `ICampaignsRepository` | DI injection in all handlers | VERIFIED | `GetForUser`, `SaveCampaign` called in CreateSession, ListSessions, GetSessionDetail, GetSessionMessages, and now action endpoint (ownership check at line 38). |
| `SessionEndpoints.cs` | `IChatHistoryRepository` | DI injection for chat history loading | VERIFIED | `CreateSession`, `GetSessionsForCampaign`, `LoadSession` called appropriately. |
| `Program.cs` | `OpenTelemetryConfiguration` | `builder.AddWretchedWhispersOpenTelemetry()` | VERIFIED | Present in Program.cs after `AddAuthorization()`. |
| `GameSessionService.cs` | `ChatCompletionAgent.InvokeStreamingAsync` | BuildKernelForSession -> CreateGameMasterAgent -> InvokeStreamingAsync -> writer.TryWrite | VERIFIED | `InvokeStreamingAsync` at line 111. `writer.TryWrite` at line 117 inside the streaming loop. Each token written to channel immediately. |
| `GameSessionService.cs` | `Channel<SseEvent>` | Producer Task writes, ProcessAction reads via ReadAllAsync | VERIFIED | `Channel.CreateUnbounded` (line 51), fire-and-forget Task (line 58), `ReadAllAsync` consumer (line 61), `writer.Complete()` in finally (line 220). |
| `GameSessionService.cs` | `ICampaignsRepository + IChatHistoryRepository` | Transactional commit after full response | VERIFIED | `BeginTransactionAsync` at line 84, `SaveMessage` at lines 89 and 145, `CommitTransactionAsync` at line 154, `RollbackTransactionAsync` at line 194. |
| `SessionEndpoints.cs` | `GameSessionService.ProcessAction` | SSE bridge in POST /sessions/{id}/actions | VERIFIED | `await foreach` over `gameService.ProcessAction(...)` at line 54, writes each event with `WriteAsync` and flushes. |
| `SessionEndpoints.cs` | `SessionConcurrencyGuard` | TryAcquire AFTER ownership check, Release in finally | VERIFIED | Ownership check at lines 38-40, `guard.TryAcquire(sessionId)` at line 43 (after ownership), `guard.Release(sessionId)` in finally block. |
| `Program.cs` | `SemanticKernelConfiguration` | `builder.Services.AddSemanticKernel(builder.Configuration)` | VERIFIED | Present in Program.cs after OTel configuration. |

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|---------|
| SESS-01 | 03-01-PLAN | User can create a new game session | SATISFIED | POST /sessions creates Campaign + ChatSession, returns 201 with SessionId. Integration tests pass. |
| SESS-02 | 03-01-PLAN | User can view list of their existing game sessions | SATISFIED | GET /sessions returns List<SessionPreviewDto> with character name, HP, status, description, lastPlayed. |
| SESS-03 | 03-01-PLAN | User can continue a saved game session | SATISFIED | GET /sessions/{id} returns SessionDetailDto with campaign state + paginated ChatHistory. |
| SESS-04 | 03-02-PLAN | Game state auto-saves after each player action | SATISFIED | Transactional commit in ExecuteAgentTurnAsync saves user message, assistant message, and any plugin-triggered domain changes atomically. Rollback on failure. |
| GAME-06 | 03-02-PLAN | Graceful error recovery when LLM fails or times out | SATISFIED | Inner try/catch in ExecuteAgentTurnAsync rolls back transaction, writes error SSE event. Resilience pipeline retries transient failures up to 2 times with exponential backoff. |
| INFR-01 | 03-02-PLAN / 03-03-PLAN | .NET API layer with SSE streaming for LLM responses | SATISFIED | Channel<SseEvent> bridge enables true token-by-token delivery: `writer.TryWrite` inside `InvokeStreamingAsync` loop (line 111-119), `channel.Reader.ReadAllAsync` yields each event as produced (line 61-64). SSE headers, flush-after-write, and done event all present. |
| INFR-04 | 03-01-PLAN | OpenTelemetry observability for API and LLM calls | SATISFIED | ASP.NET Core instrumentation + SK activity source + metrics + OTLP + console exporters. AppContext switch enables sensitive SK telemetry. Wired in Program.cs. |

**Orphaned requirements for Phase 3:** None. All 7 IDs from plans are accounted for.

---

### Gap Closure Verification

#### Gap 1: Buffered SSE delivery — CLOSED

**Previous finding:** `ExecuteAgentTurn` returned `Task<List<SseEvent>>`, buffering all events before `ProcessAction` could yield any. The HTTP client received no data until the entire LLM turn completed.

**Fix verified at:**
- `using System.Threading.Channels;` — present (line 2)
- `Channel.CreateUnbounded<SseEvent>` — line 51
- `_ = ExecuteAgentTurnAsync(sessionId, chatSessionId, playerMessage, channel.Writer, ct)` — fire-and-forget at line 58
- `writer.TryWrite(new SseEvent("narrative", new { text = content }))` — inside `InvokeStreamingAsync` loop at line 117 (not after the loop)
- `await foreach (var sseEvent in channel.Reader.ReadAllAsync(ct))` — consumer at line 61
- `writer.Complete()` — in `finally` block at line 220

Each narrative token is written to the channel inside the streaming loop. `ReadAllAsync` delivers it to the HTTP response immediately. `writer.Complete()` is always called, ensuring the consumer loop terminates cleanly.

#### Gap 2: Missing ownership check on action endpoint — CLOSED

**Previous finding:** `POST /sessions/{id}/actions` extracted `userId` but discarded it — no `GetForUser` check. Any authenticated user could invoke the agent on any session GUID.

**Fix verified at:**
- `ICampaignsRepository campaignsRepo` — DI parameter added to action endpoint lambda (line 28)
- `var userCampaigns = await campaignsRepo.GetForUser(userId)` — line 38
- `if (!userCampaigns.Any(c => c.Id == sessionId)) return Results.NotFound()` — line 39-40
- Ownership check is at line 38-40, BEFORE `guard.TryAcquire` at line 43, BEFORE SSE headers at line 50

Test `PostAction_SessionOwnedByOtherUser_ReturnsError` now asserts `HttpStatusCode.NotFound` (line 119 of SessionStreamingTests.cs). Test `PostAction_WithNonExistentSession_Returns404` also asserts `HttpStatusCode.NotFound` (line 58) — a non-existent session is correctly caught by the ownership check since it won't appear in `GetForUser` results.

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `SessionEndpoints.cs` | 156 | `lastPlayed = DateTime.UtcNow` — proxy timestamp | Warning | GET /sessions always reports current server time as lastPlayed for sessions with a chat entry, not actual last activity. Documented limitation — no timestamp tracking on chat sessions yet. |

No new anti-patterns introduced by the gap closure. The `catch` without variable capture in the outer catch block of `ExecuteAgentTurnAsync` (line 209) is intentional and clean — the variable was unused.

---

### Human Verification Required

#### 1. Token-by-Token SSE Streaming with Real LLM

**Test:** Register a user, create a session, POST /sessions/{id}/actions with a message via `curl -N` or browser EventSource. Watch for SSE events arriving progressively.
**Expected:** First `narrative` event appears within 1-3 seconds of submitting the action. Subsequent chunks arrive incrementally as the LLM generates tokens — not all at once at the end.
**Why human:** The Channel bridge is correctly wired (verified above), but timing confirmation requires a live LLM. Automated tests cannot distinguish between true streaming and rapid batched delivery without real Azure OpenAI credentials.

#### 2. Resilience Pipeline Retry Behaviour

**Test:** Configure a throttled or intentionally failing AzureOpenAI endpoint. POST an action and observe logs for retry attempts.
**Expected:** Up to 2 retries with exponential backoff (1s, ~2s with jitter) before an error SSE event is delivered. DbContext transaction is rolled back; a subsequent GET /sessions/{id} shows no state change.
**Why human:** Requires a controllable failing LLM environment. Cannot inject transient failure in current test infrastructure.

#### 3. 409 Conflict — Integration Verification

**Test:** Simultaneously send two POST /sessions/{id}/actions requests for the same session (e.g., via two concurrent curl processes or an async test that fires two requests without awaiting the first).
**Expected:** First request proceeds to SSE stream; second request returns HTTP 409 Conflict with JSON body `{"error": "GM response already in progress"}` before any SSE headers.
**Why human:** Current tests only exercise `SessionConcurrencyGuard` as a unit. No integration test fires two concurrent HTTP requests at the same session.

---

### Test Suite Results

All 239 tests pass with no failures or regressions:

```
Passed!  - Failed: 0, Passed: 239, Skipped: 0, Total: 239, Duration: 5s
```

Commits verified:
- `f951cc5` — feat(03-03): restructure GameSessionService for true token-by-token SSE streaming
- `6907471` — fix(03-03): add ownership verification to action endpoint, update tests

---

_Verified: 2026-03-03T15:45:00Z_
_Verifier: Claude (gsd-verifier)_
_Re-verification: Yes — gaps from 2026-03-03T15:10:00Z verification closed and confirmed_
