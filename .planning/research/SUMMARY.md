# Project Research Summary

**Project:** Wretched Whispers - Web-based Mork Borg RPG with LLM Game Master
**Domain:** LLM-powered text RPG with rules engine (SemanticKernel tool-calling)
**Researched:** 2026-03-02
**Confidence:** MEDIUM

## Executive Summary

Wretched Whispers is a web-based text RPG that combines LLM-driven narrative generation with a mechanically-enforced Mork Borg rules engine. The core differentiator against competitors (AI Dungeon, NovelAI) is that this system enforces real TTRPG rules through a typed domain — the LLM narrates outcomes but cannot hallucinate mechanics. The existing console prototype demonstrates functional domain logic, SemanticKernel orchestration with 20+ tool functions, and multi-agent narrative generation.

The recommended approach bridges the existing console architecture to a web context using ASP.NET Core Minimal API with Server-Sent Events (SSE) for streaming LLM responses, React/Next.js for the frontend, and SQLite for persistence. The critical architectural challenge is maintaining session isolation while preserving the connection between domain state and LLM context across save/resume cycles. The current in-memory repository pattern must transition to EF Core with careful attention to preserving DDD aggregate integrity during serialization.

The primary risks center on LLM behavior in production: tool-call hallucination (LLM inventing outcomes instead of calling domain functions), context desynchronization on session resume, and token cost explosion from auto function-calling loops. These are mitigable through authoritative state display in the UI, structured persistence of both domain state and chat history, and aggressive token budgeting with lean DTOs. The research indicates that SSE (not SignalR, not WebSockets) is the correct transport for unidirectional LLM streaming, and that the domain's repository interfaces provide clean seams for persistence refactoring.

## Key Findings

### Recommended Stack

The stack leverages existing investments (.NET 9, SemanticKernel 1.65.0, Azure OpenAI) while adding web API and frontend layers. The architecture follows a layered onion pattern with the existing DDD domain as the innermost ring.

**Core technologies:**
- **.NET 9 with ASP.NET Core Minimal API**: Already in use; low-ceremony endpoint definition for focused API surface
- **Server-Sent Events (SSE)**: Native HTTP streaming for LLM responses — SSE is the correct choice over SignalR/WebSockets because LLM responses are unidirectional, compose naturally with HTTP middleware, and match the pattern every LLM API uses
- **SQLite with EF Core 9**: Simple deployment, single-file database sufficient for concurrent solo sessions; JSON column serialization for complex domain aggregates
- **ASP.NET Core Identity**: Built-in user management with JWT bearer tokens for SPA authentication
- **Next.js 15 + React 19**: Frontend framework with App Router, SSR for landing pages, and native support for streaming responses via ReadableStream API
- **TanStack Query**: Server state management for API calls; Zustand for lightweight client-only UI state
- **OpenTelemetry**: Already integrated in console project; extend to web API for distributed tracing of LLM calls and tool invocations

**Critical version notes:** Frontend package versions (Next.js 15, React 19, Tailwind 4) are flagged as LOW confidence and need verification against current stable releases. .NET and SemanticKernel versions are HIGH confidence (verified from existing codebase).

### Expected Features

**Must have (table stakes):**
- Streamed LLM responses with typewriter effect — users expect this from AI Dungeon pattern
- Character sheet sidebar display — players need constant visibility of HP, abilities, inventory, status
- Session save/load with conversation history — users expect to close browser and resume later
- Character creation flow — guided UI showing stat rolls, name input, equipment reveal
- Basic authentication (email/password) — multi-tenant requires user identification
- Error handling for LLM failures — retry logic, graceful degradation, no silent failures
- Game session list — players with multiple campaigns need to select which to continue

**Should have (competitive differentiators):**
- Visible dice rolls with mechanical outcomes — show "d20+STR(+2)=14 vs DR 12 — HIT" alongside narrative; no competitor does this
- Mork Borg aesthetic (doom-metal dark/yellow/pink design) — CSS/typography work to match the RPG's signature look
- Calendar of Nechrubel / Misery tracker — visual doom clock showing accumulated Miseries (7-slot tracker filling up)
- Character injury status rendered visually — broken hand, lost eye, infection displayed as persistent effects
- Exportable session transcripts — download campaign as markdown/PDF for sharing

**Defer (v2+):**
- Sound design (ambient soundtrack, dice roll effects)
- Multi-agent orchestration in web (console has 3 agents; start with single agent for simplicity)
- Campaign pacing controls UI (domain supports dawn dice; LLM can handle in conversation for v1)
- Published dungeon modules (Rotblack Sludge) — focus on core rules first
- Character classes (Fanged Deserter, etc.) — ship with classless system, add later

**Anti-features (explicitly avoid):**
- Real-time multiplayer — massive complexity, PROJECT.md scopes out
- Image generation — not core to text RPG, adds cost/latency
- Custom scenario editor — nail one game system perfectly first
- Model selection — pick one model, tune it well, don't expose choice to users
- Undo/rewind — undermines Mork Borg's brutal philosophy (death is permanent)

### Architecture Approach

The architecture extends the existing DDD domain with concentric layers: application services (GameSessionService for session lifecycle management), API shell (ASP.NET Core Minimal API + SSE endpoints), and frontend client (React/Next.js SPA). The critical pattern is scoped Kernel per game session — each active session gets its own `ChatCompletionAgent` instance with isolated `ChatHistory` and domain aggregate references.

**Major components:**
1. **GameSessionService (new)** — Session lifecycle management, bridges player input to SemanticKernel agent, streams LLM output back via SSE, coordinates state persistence after each turn
2. **ASP.NET Core API Layer (new)** — HTTP endpoints for CRUD operations, SSE endpoint returning `text/event-stream` for gameplay loop, JWT authentication middleware
3. **SemanticKernel Agent (existing)** — LLM orchestration with auto function-calling, maintains ChatHistory with summarization reducer, calls plugin tools
4. **SK Plugins (existing, minor refactor)** — Typed wrappers exposing domain operations as LLM-callable tools; needs refactoring to return result types instead of throwing exceptions
5. **DDD Domain (existing, no changes)** — Game rules, character creation, combat, campaigns, calendar of Nechrubel; remains persistence-ignorant
6. **EF Core Infrastructure (new)** — SQLite persistence replacing in-memory repositories; JSON column serialization for domain aggregates to preserve DDD integrity

**Data flow (gameplay loop):**
1. Player types action in React UI
2. POST to `/api/game/{sessionId}/action` with message
3. GameSessionService looks up session's Kernel + Agent + ChatHistory
4. Agent.InvokeStreamingAsync triggers LLM with tool-calling
5. LLM calls domain tools (AttackAdversary, ChallengeCharacter, etc.)
6. Domain returns structured outcomes (hit/miss, damage, status changes)
7. LLM generates narrative response incorporating tool results
8. Streaming tokens flow back as SSE events (`data: {JSON}\n\n`)
9. React displays tokens in real-time (typewriter effect)
10. After stream completes, GameSessionService persists updated state and chat history

**Build order (component dependencies):**
1. Infrastructure/SQLite — replace in-memory repos with EF Core (unblocks everything)
2. Identity/Auth — EF Core + Identity setup for user management
3. GameSessionService — depends on repos + SK agent
4. API Layer — REST endpoints + SSE endpoint (depends on session service + auth)
5. React Frontend — depends on API being available

### Critical Pitfalls

1. **LLM tool-call hallucination and silent rule violations** — The LLM narrates outcomes without calling domain tools, causing domain state and narrative to diverge silently. Prevention: Display authoritative game state from domain alongside narrative (not LLM claims); use `FunctionChoiceBehavior.Required()` during combat to force tool calls; post-response validation comparing narrative to domain state changes.

2. **LLM context and domain state desynchronization on session resume** — Domain state (HP, inventory, encounter status) loads correctly from SQLite, but LLM's conversational memory is gone, causing narrative incoherence. Prevention: Persist ChatHistory alongside domain state; inject structured "game state recap" message on resume; test save/load cycle explicitly with integration tests.

3. **Streaming responses interleaved with tool calls** — LLM streams partial narrative, pauses for tool execution, continues streaming — results in choppy UX or tool-call metadata leaking to UI. Prevention: Use `InvokeStreamingAsync` and filter to only forward `AuthorRole.Assistant` content chunks; show "thinking" indicators during tool execution gaps; buffer tool-call chunks server-side.

4. **Guid confusion across multiple entities** — LLM passes wrong Guid to wrong function (character ID as encounter ID, wrong adversary ID). With 20+ plugin functions taking raw Guids, the LLM regularly swaps arguments. Prevention: Implement "current session context" pattern to avoid passing characterId/campaignId/encounterId to every call; allow name-based resolution for adversaries; validate and provide helpful errors listing available entities with IDs.

5. **Multi-tenant LLM cost explosion** — Each player action triggers 3-8 tool calls via auto function-calling; DTOs returned from tools are verbose (500+ tokens for full CharacterDto). With 10 concurrent players, token usage scales to 500K-1M per encounter cycle. Prevention: Token budgeting with monitoring; lean DTOs (return only changed fields, not full state); cap `MaximumAutoInvokeAttempts` to 5; per-user rate limiting.

6. **Mork Borg tone drift** — Over long sessions, LLM narration drifts from doom-metal aesthetic toward generic fantasy as system prompt influence wanes. Prevention: Append atmospheric flavor to tool return DTOs; periodic tone reinforcement via system messages; include Mork Borg lexicon (preferred/forbidden words) in system prompt; test tone preservation over 50-turn sessions.

7. **DDD aggregate serialization loses domain integrity** — The Character aggregate has deeply nested value objects (ArmorTier with 4 subclasses, BrokenOutcome with 6 subclasses, polymorphic Weapon). Naive EF Core mapping flattens the model or breaks encapsulation. Prevention: Use JSON column serialization for entire aggregates (`ToJson()` in EF Core); round-trip tests for every domain state combination; keep Core project persistence-ignorant.

## Implications for Roadmap

Based on research, suggested phase structure follows component dependencies and de-risks critical architectural challenges early.

### Phase 1: Persistence Foundation
**Rationale:** The shift from in-memory to SQLite is the foundational change that unblocks all other work. This phase de-risks the hardest technical challenge (DDD aggregate serialization) and establishes the session persistence model (domain state + chat history together).

**Delivers:**
- EF Core DbContext with SQLite configured
- JSON column serialization for Character, Campaign, Encounter aggregates
- Repository implementations replacing in-memory versions
- ChatHistory persistence alongside domain state
- Round-trip tests validating domain integrity through save/load

**Addresses (from FEATURES.md):**
- Session save/load (table stakes)
- Database migration path from current prototype

**Avoids (from PITFALLS.md):**
- Pitfall #7: DDD aggregate serialization breaking domain
- Pitfall #2: State desynchronization on resume (partially — establishes data model)

**Research flag:** Standard pattern — EF Core with SQLite is well-documented. No phase-specific research needed, but aggressive round-trip testing required.

### Phase 2: Authentication and User Management
**Rationale:** Multi-tenant capability (users owning multiple game sessions) is a prerequisite for web deployment. Authentication must come before the API layer so endpoints can enforce user ownership of sessions.

**Delivers:**
- ASP.NET Core Identity integrated with EF Core
- User registration, login, token refresh endpoints (Identity API)
- JWT bearer token authentication for API
- User-to-session ownership model in database
- Basic authorization middleware

**Addresses (from FEATURES.md):**
- Basic authentication (email/password) — table stakes

**Uses (from STACK.md):**
- ASP.NET Core Identity with JWT bearer tokens
- Identity.EntityFrameworkCore for SQLite integration

**Avoids (from PITFALLS.md):**
- Multi-tenant isolation (establishes user context for all subsequent work)

**Research flag:** Standard pattern — Identity API endpoints are well-documented. Skip phase research.

### Phase 3: Core API and Streaming Bridge
**Rationale:** This phase builds the novel architectural component — bridging SemanticKernel's `IAsyncEnumerable<StreamingChatMessageContent>` to SSE streaming over HTTP. This is the highest technical risk and should be prototyped early to surface issues with streaming + tool-calling interplay.

**Delivers:**
- GameSessionService (session scoping, Kernel-per-session)
- SSE endpoint for gameplay loop (`/api/game/{id}/action`)
- REST endpoints for session CRUD, character state queries
- Streaming token buffering and tool-call filtering
- Error handling for LLM failures (retry logic, timeouts)
- OpenTelemetry integration for web API

**Addresses (from FEATURES.md):**
- Streamed LLM responses — table stakes
- Error handling and loading states — table stakes
- Game session list — table stakes

**Uses (from STACK.md):**
- ASP.NET Core Minimal API
- Server-Sent Events (native HTTP)
- SemanticKernel InvokeStreamingAsync

**Implements (from ARCHITECTURE.md):**
- Scoped Kernel per game session pattern
- Application service layer (GameSessionService)
- SSE endpoint returning text/event-stream

**Avoids (from PITFALLS.md):**
- Pitfall #3: Streaming responses interleaved with tool calls
- Pitfall #8: Unbounded chat history (tune summarization thresholds)
- Pitfall #11: Concurrent requests to same session (per-session locking)

**Research flag:** Needs phase-specific research — SSE with ASP.NET Core + SemanticKernel streaming is not a well-documented combination. Plan 2-3 days for prototyping and integration testing.

### Phase 4: Semantic Layer Hardening
**Rationale:** The existing plugins are designed for single-user console prototype with exception-based error handling. Before exposing to web with LLM-driven inputs, plugins need refactoring to return result types and reduce Guid parameter burden.

**Delivers:**
- Plugin methods return result types instead of throwing exceptions
- Descriptive error messages with available entity context
- "Current session context" pattern to reduce Guid parameters
- Name-based resolution for adversaries
- Token budgeting: lean DTOs returning only changed fields
- Tool-call validation and helpful error messages

**Addresses (from PITFALLS.md):**
- Pitfall #4: Guid confusion (simplified parameter model)
- Pitfall #9: Exception-driven error handling crashes flow
- Pitfall #5: Cost explosion (lean DTOs established)

**Avoids (from PITFALLS.md):**
- Tool-call retry loops from bad inputs
- Token waste from verbose DTOs

**Research flag:** Domain-specific refactor — no external research needed, but requires careful domain knowledge. Budget extra time for testing tool-call reliability.

### Phase 5: Character Creation and Onboarding Flow
**Rationale:** Character creation is the first thing every player does and must feel polished. This phase delivers the first complete user-facing feature end-to-end (frontend + API + domain).

**Delivers:**
- Character creation API endpoints
- React character creation wizard (guided UI)
- Display of stat rolls, name input, equipment reveal
- Campaign creation flow (dawn dice selection, initial setup)
- Session initialization with first LLM interaction
- Loading indicators and error states

**Addresses (from FEATURES.md):**
- Character creation flow — table stakes
- Campaign creation — table stakes
- First-time user experience

**Uses (from STACK.md):**
- Next.js with React
- TanStack Query for API state
- Tailwind CSS for styling

**Research flag:** Standard web form flow — skip phase research.

### Phase 6: Frontend Core Gameplay Interface
**Rationale:** With backend streaming and session management complete, build the primary game UI — chat interface, character sheet sidebar, and streaming text display.

**Delivers:**
- Chat interface with text input for player actions
- SSE consumption via fetch + ReadableStream
- Streaming text display with typewriter effect
- Character sheet sidebar (HP, abilities, inventory, status)
- Message history with scrollback
- Mobile-responsive layout
- Mork Borg aesthetic (dark theme, doom-metal typography)

**Addresses (from FEATURES.md):**
- Text input for player actions — table stakes
- Character sheet display — table stakes
- Message history / scrollback — table stakes
- Responsive text layout — table stakes

**Uses (from STACK.md):**
- React with custom useGameStream hook
- react-markdown for safe narrative rendering
- Zustand for client state (streaming buffer)
- EventSource API / fetch ReadableStream

**Avoids (from PITFALLS.md):**
- Pitfall #10: LLM output rendered unsafely (strict markdown only)

**Research flag:** Standard React patterns with SSE — well-documented. Skip phase research.

### Phase 7: Mechanical Visibility and Differentiators
**Rationale:** This phase surfaces the core differentiator — the mechanically-enforced rules. Display dice rolls, combat outcomes, and status tracking to show players that rules are real, not hallucinated.

**Delivers:**
- Dice roll display alongside narrative ("d20+STR(+2)=14 vs DR 12 — HIT")
- Combat outcome structured display (damage dealt, fumble/critical indicators)
- Misery tracker visual component (7-slot doom clock)
- Character injury/status indicators (broken limbs, infection, equipment degradation)
- Authoritative state display (UI always shows domain truth, not LLM claims)
- Post-response validation (detect narrative/state divergence)

**Addresses (from FEATURES.md):**
- Visible dice rolls / mechanical outcomes — differentiator
- Misery tracker — differentiator
- Character injury status — differentiator

**Avoids (from PITFALLS.md):**
- Pitfall #1: Tool-call hallucination (authoritative state display prevents silent divergence)

**Research flag:** Mork Borg rules interpretation — some ambiguity in how to present combat outcomes visually. May need spot research for UI design patterns in similar games.

### Phase 8: Polish and Production Readiness
**Rationale:** Final phase focuses on reliability, observability, and user experience refinements before launch.

**Delivers:**
- Tone preservation system (word palette, periodic reinforcement messages)
- History summarization tuning (aggressive thresholds for web)
- Token usage monitoring and per-user rate limiting
- Session timeout and cleanup for idle sessions
- Background summarization (async, not blocking requests)
- Reconnection handling for SSE
- Comprehensive error handling and user-facing error messages
- Integration tests for full gameplay flows (create, play, save, resume)
- E2E tests with Playwright

**Addresses (from FEATURES.md):**
- Production-quality error handling
- Loading/thinking indicators

**Uses (from STACK.md):**
- OpenTelemetry for monitoring
- Vitest + Playwright for testing

**Avoids (from PITFALLS.md):**
- Pitfall #6: Tone drift (tone preservation system)
- Pitfall #5: Cost explosion (monitoring and rate limits enforced)
- Pitfall #13: Prompt injection (domain invariants tested, system prompt hardened)

**Research flag:** LLM production patterns — may need research on tone preservation techniques and token optimization strategies for long-running sessions.

### Phase Ordering Rationale

- **Persistence comes first** because it unblocks all other work and de-risks the hardest serialization problem
- **Auth before API** ensures multi-tenant isolation from the start
- **API/streaming before frontend** because frontend needs working endpoints to integrate against
- **Semantic layer hardening before heavy frontend work** prevents churn from API contract changes
- **Character creation before gameplay UI** because it's the entry point (test full stack with bounded scope)
- **Differentiators (Phase 7) after core loop works** — prove basic gameplay first, then surface mechanical visibility
- **Polish last** because it requires full system to tune (token budgets, tone preservation)

The critical path is Phases 1-3 (persistence → auth → API/streaming). Phase 3 has highest technical risk and should be prototyped early. Phases 5-6 can partially overlap (frontend team can start mocking API responses while backend completes). Phase 7 delivers the product's core value proposition and should not be cut.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Core API and Streaming Bridge):** Complex integration of SSE + SemanticKernel streaming + tool-calling. Not a standard documented pattern. Plan prototype sprint to validate approach.
- **Phase 8 (Polish and Production Readiness):** Tone preservation techniques for LLMs and token optimization for long sessions may need research into production LLM application patterns.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Persistence Foundation):** EF Core + SQLite is well-documented
- **Phase 2 (Authentication):** ASP.NET Core Identity API endpoints are official and documented
- **Phase 4 (Semantic Layer Hardening):** Domain-specific refactor, no external research needed
- **Phase 5 (Character Creation):** Standard web form flow
- **Phase 6 (Frontend Core):** React + SSE patterns are documented

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM-HIGH | .NET/SemanticKernel versions are HIGH (verified from codebase). Frontend versions (Next.js 15, React 19, Tailwind 4) are LOW (need verification). SSE recommendation is HIGH (strong architectural rationale). |
| Features | MEDIUM | Table stakes identified from competitor analysis (AI Dungeon patterns well-known). Differentiators clear from domain analysis. Mork Borg-specific features verified from existing domain code. No live competitor research possible. |
| Architecture | HIGH | Component boundaries derived from codebase analysis. Scoped Kernel pattern is sound. SSE over SignalR rationale is strong. Build order follows clear dependency graph. |
| Pitfalls | MEDIUM-HIGH | Critical pitfalls (#1-7) identified from codebase analysis and LLM application patterns. Specific to this domain (tool-calling, DDD persistence, streaming). Some recommendations (tone preservation, token optimization) need validation during implementation. |

**Overall confidence:** MEDIUM-HIGH

The architecture and stack recommendations are sound (based on existing codebase analysis and established patterns). The main uncertainty is in frontend package versions (need current stable version verification) and LLM production behavior at scale (tone preservation, token optimization). The research provides clear direction but some aspects require validation during implementation.

### Gaps to Address

**Frontend package versions:** Next.js, React, Tailwind, TanStack Query, Zustand versions are based on training data (cutoff May 2025). Before creating frontend, verify latest stable versions on npmjs.com.

**Azure OpenAI rate limits and pricing:** Current token-per-minute limits and pricing need verification for cost modeling. This affects Phase 8 rate-limiting implementation.

**SemanticKernel streaming behavior with tool calls:** The interaction between `InvokeStreamingAsync` and auto function-calling mid-stream is not extensively documented. Plan prototype in Phase 3 to validate before committing to SSE approach.

**Mork Borg Third-Party License compliance:** Review license requirements before public release. Ensure system prompt does not instruct LLM to reproduce published content.

**EF Core JSON column support for complex nested types:** The domain has deeply nested polymorphic value objects (ArmorTier subclasses). While EF Core 7+ supports `ToJson()`, test early in Phase 1 that it handles the specific domain shapes without custom converters.

**Tone preservation effectiveness:** The recommendation to append flavor text to tool DTOs and use periodic tone reinforcement is based on general LLM patterns. Effectiveness for maintaining Mork Borg aesthetic over 50+ turn sessions needs empirical validation during Phase 8.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: `/home/arst/Projects/wretched_whispers/` — full domain, semantic, and infrastructure layers reviewed
- PROJECT.md requirements and constraints
- SemanticKernel 1.65.0 (verified from csproj)
- .NET 9 (verified from csproj TargetFramework)
- ASP.NET Core official documentation — SSE, Identity API, Minimal API patterns

### Secondary (MEDIUM confidence)
- AI Dungeon/NovelAI feature sets (training data through early 2025)
- LLM application architecture patterns (tool-calling, streaming, token optimization)
- EF Core DDD persistence patterns
- React + SSE integration patterns

### Tertiary (LOW confidence)
- Frontend package versions (Next.js 15, React 19, Tailwind 4) — training data May 2025 cutoff, needs verification
- Azure OpenAI rate limits and pricing — training data, needs current verification
- Mork Borg competitor landscape 2026 — no live research available

---
*Research completed: 2026-03-02*
*Ready for roadmap: yes*
