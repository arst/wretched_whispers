# Phase 3: API Layer and Streaming - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

REST endpoints for game session management (create, list, resume) and an SSE streaming bridge that delivers LLM narrator responses token-by-token to the client. This phase exposes the existing domain and SemanticKernel layer over HTTP with authentication — it does not add new game mechanics, UI, or multi-agent orchestration.

</domain>

<decisions>
## Implementation Decisions

### Game session API design
- Single endpoint to create a session (POST). No separate character creation step — the GM guides character creation conversationally as the first interaction
- Session maps 1:1 to Campaign (no separate session abstraction layer)
- Session list (GET) returns rich previews: session ID, character name, class, HP, campaign description snippet, last played date, session status (in-progress, character-creation, ended)
- Resuming a session returns character/campaign state + last N chat messages (paginated). Client can fetch older messages on scroll-up via a separate endpoint

### Streaming & tool visibility
- Per-turn SSE: client opens SSE connection when submitting an action, receives the streamed response, connection closes when the GM finishes. No persistent long-lived connections
- Tool calls (dice rolls, combat, state mutations) emit as separate structured SSE events alongside narrative text — "mechanical sidebar" pattern. The client decides how to display them
- Stream emits typed events: `narrative` (text chunks), `tool_result` (mechanical outcomes), `state_update` (game state deltas like HP changes, inventory updates). Client stays in sync without re-fetching
- Single ChatCompletionAgent (GM only) for this phase. No HandoffOrchestration or multi-agent setup yet

### Player action submission
- Free-text input only. Player types natural language, the GM interprets intent and calls the appropriate domain tools. No structured action commands
- POST /sessions/{id}/actions returns the SSE stream directly (Content-Type: text/event-stream). One request = one streamed response
- 409 Conflict returned if a GM response is already in progress for the same session. Prevents double-submit and multi-tab conflicts
- GM sends the first message automatically when a new session is created. Player's first action is a response to the GM's introduction

### Failure & recovery
- On LLM error mid-stream: send an error event on the SSE stream, discard the partial response. Game state is not modified (all changes rolled back)
- All state changes (tool call results, domain mutations) are buffered and committed to the database only after the GM completes the full response successfully. True transactional behavior
- Server-side retry on transient LLM failures (rate limits, network blips): 2-3 attempts before returning an error event to the client. Player doesn't see transient blips
- On unrecoverable failure: structured error event with a message the client can display. Game state remains at the last committed point

### Claude's Discretion
- GM response timeout value (reasonable default, configurable)
- SSE event format details (field naming, JSON structure)
- Chat history page size for paginated resume
- Retry backoff strategy for transient LLM failures
- How to handle the SK Kernel/agent lifecycle per request (scoped vs pooled)

</decisions>

<specifics>
## Specific Ideas

- The existing SingleAgent.Console uses `await foreach (InvokeAsync)` with IAsyncEnumerable — this is the streaming pattern to bridge into SSE
- The existing console GM prompt/instructions should be reused as-is for the API's agent
- ChatHistorySummarizationReducer is already implemented in SingleAgent.Console — should be wired into the API agent for long-running sessions

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ServiceCollectionExtensions.AddDomainServices()`: Scoped DI registration for web API (repos, domain services, dice, JSON options)
- `ServiceCollectionExtensions.AddSqliteInfrastructure()`: Transient DI for SK plugin resolution
- SK Plugins: `CharacterPlugin`, `CampaignPlugin`, `EncounterPlugin`, `DicePlugin` — ready to import into a Kernel
- `SqliteChatHistoryRepository`: Persists chat messages as individual rows with FunctionCallContent serialization
- `ChatHistorySummarizationReducer`: Context window management for long sessions (SingleAgent.Console)
- `WretchedWhispersDbContext` with Identity: Already configured with IdentityUserContext, CampaignEntity.UserId FK

### Established Patterns
- Two DI paths: `AddDomainServices` (Scoped for web API) vs `AddSqliteInfrastructure` (Transient for SK). API will need both or a merged approach
- Bearer token auth already configured (60min access, 14-day refresh) via Identity API endpoints
- JSON blob persistence: Guid Id PK + string Data TEXT per aggregate table
- AzureOpenAI as LLM provider via `settings.AzureOpenAi.*`

### Integration Points
- API project (WretchedWhispers.Api) already has auth + health endpoint — new session endpoints extend this
- CampaignEntity.UserId FK provides multi-tenant filtering for session list/access
- `Kernel.ImportPluginFromType<T>()` pattern for registering domain plugins with the agent
- `ChatCompletionAgent.InvokeAsync()` returns `IAsyncEnumerable<ChatMessageContent>` — bridge to SSE

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-api-layer-and-streaming*
*Context gathered: 2026-03-03*
