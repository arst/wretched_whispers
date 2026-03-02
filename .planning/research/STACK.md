# Technology Stack

**Project:** Wretched Whispers - Web-based Mork Borg RPG with LLM Game Master
**Researched:** 2026-03-02
**Overall Confidence:** MEDIUM (versions verified against existing codebase where possible; npm/NuGet versions from training data flagged for verification)

## Research Limitations

Web search and web fetch tools were unavailable during this research session. Version numbers for new dependencies (not already in the codebase) are based on training data with a cutoff of May 2025 and are flagged as LOW confidence. **Before installing packages, verify latest versions on nuget.org and npmjs.com.** Existing codebase versions (SemanticKernel 1.65.0, .NET 9) are HIGH confidence as they come directly from the project files.

---

## Recommended Stack

### Backend - ASP.NET Core Web API

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET | 9.0 | Runtime | Already in use (verified from csproj). LTS is .NET 8, but project already targets net9.0. Stick with 9.0 for consistency with existing Semantic/Orchestration/Infrastructure projects. | HIGH |
| ASP.NET Core Minimal API | 9.0 (built-in) | HTTP API layer | Minimal APIs are the modern .NET approach for clean, low-ceremony endpoints. No need for controller overhead in a game with a focused API surface. Use `app.MapGroup()` for route organization. | HIGH |
| Microsoft.SemanticKernel | 1.65.0 | LLM orchestration | Already in use. Pin to project's current version for consistency. Supports streaming via `InvokeStreamingAsync` and `GetStreamingChatMessageContentsAsync`. | HIGH |
| Microsoft.SemanticKernel.Connectors.AzureOpenAI | 1.65.0 | Azure OpenAI connector | Already in use. Provides `AzureOpenAIPromptExecutionSettings` with `FunctionChoiceBehavior.Auto()` for tool calling. | HIGH |

### Real-time Streaming

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Server-Sent Events (SSE) | Native HTTP | LLM response streaming | **Use SSE, not SignalR, not WebSockets.** SSE is the correct transport for LLM streaming because: (1) LLM responses are unidirectional server-to-client text streams, which is exactly what SSE does, (2) every LLM API (OpenAI, Azure OpenAI, Anthropic) uses SSE natively, so the pattern composes cleanly, (3) Next.js and the Fetch API have native SSE/ReadableStream support, (4) simpler than SignalR (no connection negotiation, no hub protocol), (5) works through proxies and CDNs without configuration. Player input goes via normal POST requests; narrative streams back via SSE. | HIGH |

**Why NOT SignalR:** SignalR adds bidirectional messaging, connection groups, reconnection logic, and a hub abstraction. None of these are needed here. The game interaction is request-response with a streaming response body, not a persistent bidirectional channel. SignalR would add complexity (client library, hub classes, connection management) without benefit. Real-time multiplayer would warrant SignalR; solo sessions with streamed responses do not.

**Why NOT raw WebSockets:** WebSockets are lower-level than needed. They require manual message framing, reconnection handling, and don't compose with HTTP middleware (auth, CORS, etc.) as cleanly. SSE rides on normal HTTP, gets auth headers for free, and reconnects automatically via the `EventSource` API.

**Implementation pattern:**
```csharp
// ASP.NET Core endpoint returning SSE
app.MapPost("/api/game/{sessionId}/action", async (
    string sessionId,
    PlayerActionRequest request,
    GameSessionService service,
    HttpContext context) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    await foreach (var chunk in service.ProcessActionStreaming(sessionId, request))
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
        await context.Response.Body.FlushAsync();
    }
});
```

### Database and Persistence

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.x | SQLite ORM | EF Core is the standard .NET ORM. SQLite version matches .NET 9 target. Project requirement specifies SQLite for simple deployment. | MEDIUM (version needs verification) |
| Microsoft.EntityFrameworkCore.Design | 9.0.x | EF migrations tooling | Required for `dotnet ef migrations add/update`. Dev dependency only. | MEDIUM |
| SQLite | embedded via EF | Game state persistence | Replaces current in-memory repositories. Single file database, zero configuration, xcopy deployable. Sufficient for single-server deployment with concurrent solo sessions. | HIGH |

**Data to persist:**
- User accounts (Identity)
- Game sessions (metadata, active/archived status)
- Character state (serialized domain aggregate)
- Chat history (messages for session continuity)
- Campaign state (world state, miseries calendar)
- Encounter state

**Migration path from in-memory:** The existing repository interfaces (`ICharactersRepository`, `ICampaignsRepository`, `IEncountersRepository`) provide clean seams. Implement new `EfCharactersRepository`, `EfCampaignsRepository`, `EfEncountersRepository` behind the same interfaces. Add an `AddSqliteInfrastructure` extension method alongside the existing `AddInMemoryInfrastructure`.

### Authentication

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| ASP.NET Core Identity | 9.0.x (built-in) | User management | Built into ASP.NET Core. Handles password hashing (PBKDF2), account lockout, email confirmation, and user storage. No third-party auth library needed. | HIGH |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.x | Identity + EF Core integration | Stores Identity data in SQLite via EF Core. Single database for everything. | MEDIUM (version verify) |
| JWT Bearer tokens | built-in | API authentication | Stateless auth for the SPA frontend. The Next.js app stores JWT in httpOnly cookies (not localStorage). ASP.NET Core has built-in JWT validation middleware. | HIGH |

**Why NOT cookie auth:** Cookie auth works but couples the auth flow to the ASP.NET server. JWT allows the Next.js frontend to be deployed separately and scales to multiple backend instances if needed later.

**Why NOT OAuth/OpenID Connect:** Explicitly out of scope per PROJECT.md. Email/password only for v1.

**Auth flow:**
1. POST `/api/auth/register` - creates account
2. POST `/api/auth/login` - returns JWT access token + refresh token
3. Frontend stores tokens in httpOnly cookies
4. All API calls include `Authorization: Bearer {token}`
5. SSE endpoints validate JWT from query param or cookie (EventSource API cannot set custom headers)

### Frontend - Next.js + React

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| Next.js | 15.x | React framework | App Router with React Server Components. Provides file-based routing, SSR for initial load, and API routes for BFF (Backend-for-Frontend) pattern if needed. The project is primarily a SPA with streaming, but Next.js gives SEO for landing/marketing pages and a clean project structure. | LOW (verify current stable version) |
| React | 19.x | UI library | Ships with Next.js 15. React 19 brings `use()` hook, Server Components, and improved Suspense -- all useful for streaming game state. | LOW (verify current version) |
| TypeScript | 5.x | Type safety | Non-negotiable for a project with complex game state. Catches type mismatches between frontend and backend DTOs at compile time. | MEDIUM |
| Tailwind CSS | 4.x | Styling | Utility-first CSS. Fast iteration for a text-heavy game UI. No component library needed -- this is a custom dark-themed game, not a dashboard. Build the aesthetic from scratch with Tailwind utilities. | LOW (verify v4 stability) |

**Why NOT a component library (MUI, shadcn/ui, Radix):** The game UI is bespoke -- scrolling narrative text, character sheets, doom-metal aesthetics. Pre-built component libraries optimize for conventional UIs (forms, tables, dashboards). The few standard UI elements needed (buttons, inputs, modals) are trivial to build with Tailwind. Using a component library would fight the aesthetic rather than support it.

**Why NOT plain React (Create React App / Vite):** Next.js provides file-based routing, SSR for the landing page, and middleware for auth checks. Vite + React Router would work but requires more manual setup for routing and SSR. The overhead of Next.js is minimal for the benefits.

### Frontend - State Management and Data Fetching

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| TanStack Query (React Query) | 5.x | Server state management | Handles API calls, caching, loading/error states, and refetching. The game has clear server state (sessions, characters) that maps perfectly to React Query's model. | MEDIUM |
| Zustand | 5.x | Client state management | Lightweight store for UI-only state: current streaming text buffer, sidebar toggle, theme preferences. React Query handles server state; Zustand handles the rest. Simpler than Redux, no boilerplate. | LOW (verify current version) |
| EventSource API / fetch + ReadableStream | Native browser | SSE consumption | No library needed. The browser's native `EventSource` API handles SSE reconnection. For POST-based SSE (which EventSource doesn't support), use `fetch()` with `response.body.getReader()` to process the ReadableStream. | HIGH |

**Why NOT Redux:** Overkill for this application. The game state lives on the server (the .NET domain is the source of truth). The frontend is a thin view layer that displays streamed narrative and sends player actions. Zustand's 2KB footprint and zero-boilerplate API fit perfectly.

**Why NOT SWR:** TanStack Query has more features (mutations, optimistic updates, infinite queries for chat history scrollback) and better DevTools. SWR is lighter but the feature gap matters here.

### Frontend - Streaming Text Display

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| react-markdown | 9.x | Markdown rendering | The LLM Game Master's narrative may include basic formatting (bold, italic, lists). react-markdown renders this safely without dangerouslySetInnerHTML. | LOW (verify version) |
| Custom streaming hook | N/A | SSE consumption + text buffering | Build a `useGameStream` hook that: (1) POSTs player action, (2) reads the SSE response stream, (3) buffers chunks into displayable text, (4) exposes streaming state (idle/streaming/error). This is ~50 lines of custom code, not a library concern. | HIGH |

### Testing

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| xUnit | (existing) | .NET unit/integration tests | Already in use (WretchedWhispers.Tests project exists). | HIGH |
| Vitest | 3.x | Frontend unit tests | Fast, Vite-native test runner. Works with TypeScript out of the box. | LOW (verify version) |
| Playwright | 1.x | E2E tests | Cross-browser E2E testing. Useful for testing the full game flow (login, create session, play). | LOW (verify version) |
| Testing Library | 16.x | React component tests | `@testing-library/react` for component-level tests. Encourages testing behavior over implementation. | LOW (verify version) |

### Observability

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| OpenTelemetry | 1.13.x | Distributed tracing and metrics | Already integrated in the console project. Extend to the web API. SemanticKernel has built-in OTel support (already enabled via `EnableOTelDiagnosticsSensitive`). | HIGH |
| OpenTelemetry.Exporter.Otlp | 1.13.x | OTLP export | Export traces to Jaeger, Grafana, or Aspire Dashboard during development. Replace console exporter for the web project. | MEDIUM |
| Serilog | 4.x | Structured logging | Standard .NET structured logging. Sinks for console, file, and OpenTelemetry. Replaces default `ILogger` with richer configuration. | LOW (verify version) |

### Development Tooling

| Technology | Version | Purpose | Why | Confidence |
|------------|---------|---------|-----|------------|
| .NET Aspire | 9.x | Dev orchestration | Orchestrates the .NET backend and monitors telemetry during development. Built-in dashboard for traces, logs, and metrics. Optional but significantly improves the dev experience for a multi-service setup. | LOW (verify current version) |
| ESLint + Prettier | latest | Frontend code quality | Standard JS/TS linting and formatting. Non-negotiable for team projects, still valuable for solo. | MEDIUM |
| Husky + lint-staged | latest | Pre-commit hooks | Run linting on staged files before commit. Prevents broken code from entering the repo. | MEDIUM |

---

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Streaming transport | SSE | SignalR | Bidirectional messaging not needed; SSE is simpler and composes with HTTP auth/middleware natively |
| Streaming transport | SSE | WebSockets | Lower-level than needed; manual framing and reconnection; doesn't compose with HTTP middleware |
| Streaming transport | SSE | gRPC streaming | Excellent for service-to-service but poor browser support without grpc-web proxy; over-engineered for this use case |
| Frontend framework | Next.js | Vite + React Router | Next.js provides SSR, file routing, and middleware out of the box; marginal overhead for significant convenience |
| Frontend framework | Next.js | Remix | Good alternative but smaller ecosystem; Next.js is the dominant React framework |
| Frontend framework | Next.js | Blazor | Would keep everything in C# but Blazor WASM has large payload and the React ecosystem for text/chat UIs is far richer |
| ORM | EF Core | Dapper | EF Core provides migrations, change tracking, and Identity integration; Dapper is faster but raw SQL for every query adds maintenance burden |
| ORM | EF Core | Marten (document DB on Postgres) | Requires PostgreSQL which is out of scope; document model is appealing for game state but SQLite is the constraint |
| Auth | ASP.NET Identity + JWT | Auth0/Clerk | External dependency, cost at scale, and over-engineered for email/password only; ASP.NET Identity is built-in and sufficient |
| Auth | ASP.NET Identity + JWT | Cookie auth | Couples frontend deployment to backend server; JWT is more flexible for separate frontend/backend |
| State management | Zustand | Redux Toolkit | More boilerplate than needed; game state is server-authoritative so client state is minimal |
| State management | TanStack Query | SWR | TanStack Query has richer mutation/cache invalidation features needed for game actions |
| CSS | Tailwind CSS | CSS Modules | Tailwind's utility approach is faster for custom dark-themed UIs; CSS Modules work but slower iteration |
| CSS | Tailwind CSS | styled-components | Runtime CSS-in-JS has performance overhead; Tailwind compiles away at build time |
| Database | SQLite | LiteDB | Less ecosystem support, no EF Core integration, smaller community |

---

## Architecture Sketch

```
Browser (Next.js SPA)
    |
    |--- POST /api/auth/* (JWT auth)
    |--- POST /api/game/{id}/action (player input)
    |         |
    |         +--- SSE response stream (narrative chunks)
    |
    |--- GET /api/game/sessions (list games)
    |--- POST /api/game/sessions (create game)
    |--- GET /api/game/{id}/state (character, inventory, etc.)
    |
ASP.NET Core Minimal API (.NET 9)
    |
    +--- Auth middleware (JWT validation)
    +--- GameSessionService (orchestrates game flow)
    |       |
    |       +--- SemanticKernel (ChatCompletionAgent + plugins)
    |       |       |
    |       |       +--- CharacterPlugin, CampaignPlugin, EncounterPlugin, DicePlugin
    |       |       +--- Azure OpenAI (streaming chat completion)
    |       |
    |       +--- Domain layer (WretchedWhispers.Core)
    |       +--- EF Core + SQLite (persistence)
    |
    +--- ASP.NET Identity (user management)
    +--- OpenTelemetry (observability)
```

---

## Key Integration Points

### 1. SemanticKernel Streaming to SSE

The existing code uses `gameMasterAgent.InvokeAsync()` which returns complete messages. For streaming, use `InvokeStreamingAsync()`:

```csharp
// Current (non-streaming):
await foreach (ChatMessageContent response in gameMasterAgent.InvokeAsync(message, agentThread))
    Console.WriteLine(response.Content);

// Web streaming equivalent:
await foreach (StreamingChatMessageContent chunk in gameMasterAgent.InvokeStreamingAsync(message, agentThread))
{
    // Write SSE event to HTTP response
    await WriteSSEEvent(httpContext.Response, chunk.Content);
}
```

The SemanticKernel `ChatCompletionAgent` supports `InvokeStreamingAsync` which yields `StreamingChatMessageContent` chunks. These map directly to SSE events.

### 2. Session Isolation (Multi-tenant)

Each game session needs its own:
- `ChatCompletionAgent` instance (with its own `ChatHistory` and `HistoryReducer`)
- Domain aggregate references (Character, Campaign, Encounter)
- Thread/history state

**Pattern:** Create a `GameSession` class that owns the agent, kernel, and domain state. Store active sessions in a `ConcurrentDictionary<string, GameSession>`. Persist to SQLite on each action. Rehydrate from SQLite when a session is resumed.

### 3. Core Project Target Framework

The `WretchedWhispers.Core` project currently targets `net8.0` while other projects target `net9.0`. This works fine (net9.0 projects can reference net8.0 libraries), but should be unified to `net9.0` for consistency when adding the web API project.

---

## Installation Plan

### Backend (new Web API project)

```bash
# Create new web API project
dotnet new web -n WretchedWhispers.Web -f net9.0

# Add project references
dotnet add WretchedWhispers.Web reference ../WretchedWhispers.Core
dotnet add WretchedWhispers.Web reference ../WretchedWhispers.Infrastructure
dotnet add WretchedWhispers.Web reference ../WretchedWhispers.Semantic

# Core packages
dotnet add WretchedWhispers.Web package Microsoft.SemanticKernel --version 1.65.0
dotnet add WretchedWhispers.Web package Microsoft.SemanticKernel.Connectors.AzureOpenAI --version 1.65.0
dotnet add WretchedWhispers.Web package Microsoft.SemanticKernel.Agents.Core --version 1.65.0

# Database
dotnet add WretchedWhispers.Web package Microsoft.EntityFrameworkCore.Sqlite
dotnet add WretchedWhispers.Web package Microsoft.EntityFrameworkCore.Design

# Authentication
dotnet add WretchedWhispers.Web package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add WretchedWhispers.Web package Microsoft.AspNetCore.Authentication.JwtBearer

# Observability
dotnet add WretchedWhispers.Web package OpenTelemetry.Extensions.Hosting
dotnet add WretchedWhispers.Web package OpenTelemetry.Exporter.OpenTelemetryProtocol

# (Optional) Structured logging
dotnet add WretchedWhispers.Web package Serilog.AspNetCore
```

### Frontend

```bash
# Create Next.js project
npx create-next-app@latest wretched-whispers-web --typescript --tailwind --eslint --app --src-dir

# Core dependencies
npm install @tanstack/react-query zustand react-markdown

# Dev dependencies
npm install -D @types/node prettier eslint-config-prettier
```

---

## Version Verification Checklist

The following versions could NOT be verified against live package registries and should be checked before use:

| Package | Claimed Version | Verify At | Priority |
|---------|----------------|-----------|----------|
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.x | nuget.org | HIGH |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.x | nuget.org | HIGH |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.x | nuget.org | HIGH |
| Next.js | 15.x | npmjs.com/package/next | HIGH |
| React | 19.x | npmjs.com/package/react | HIGH |
| Tailwind CSS | 4.x | npmjs.com/package/tailwindcss | MEDIUM |
| TanStack Query | 5.x | npmjs.com/package/@tanstack/react-query | MEDIUM |
| Zustand | 5.x | npmjs.com/package/zustand | MEDIUM |
| react-markdown | 9.x | npmjs.com/package/react-markdown | LOW |
| Vitest | 3.x | npmjs.com/package/vitest | LOW |

**Verified from existing codebase (HIGH confidence):**
- .NET 9.0 (from csproj TargetFramework)
- Microsoft.SemanticKernel 1.65.0 (from csproj PackageReference)
- Microsoft.SemanticKernel.Agents.Core 1.65.0 (from csproj)
- Microsoft.SemanticKernel.Connectors.AzureOpenAI 1.65.0 (from csproj)
- OpenTelemetry 1.13.1 (from csproj)

---

## Sources

- `/home/arst/Projects/wretched_whispers/WrtechedWhispers/WretchedWhispers.Semantic/WretchedWhispers.Semantic.csproj` - SemanticKernel version
- `/home/arst/Projects/wretched_whispers/WrtechedWhispers/WretchedWhispers.SingleAgent.Console/WretchedWhispers.SingleAgent.Console.csproj` - Full package list
- `/home/arst/Projects/wretched_whispers/WrtechedWhispers/WretchedWhispers.SingleAgent.Console/Program.cs` - Current agent + streaming pattern
- `/home/arst/Projects/wretched_whispers/WrtechedWhispers/WretchedWhispers.Infrastructure/ServiceCollectionExtensions.cs` - DI registration pattern
- `/home/arst/Projects/wretched_whispers/.planning/PROJECT.md` - Project requirements and constraints
- Training data (May 2025 cutoff) - Frontend ecosystem versions, ASP.NET Core patterns. **Flagged as needing verification.**
