# Architecture Patterns

**Domain:** Web-based text RPG with LLM Game Master (Mork Borg)
**Researched:** 2026-03-02

## Recommended Architecture

The architecture follows a **Layered Onion** pattern, preserving the existing DDD domain as the innermost ring and adding concentric layers outward: application services, an API shell, and a frontend client. The critical architectural decision is **how to bridge the SemanticKernel agent (which today runs in a console loop) into a request/response web model with streaming**.

```
+---------------------------+
|     React/Next.js SPA     |  Browser
+---------------------------+
         |  SignalR (streaming)
         |  REST (CRUD, auth)
+---------------------------+
|   ASP.NET Core Web API    |  API Layer
|  +---------------------+  |
|  | GameSessionService   |  |  Application Layer (new)
|  | AuthService          |  |
|  +---------------------+  |
|  +---------------------+  |
|  | SemanticKernel Agent |  |  LLM Orchestration (existing)
|  | SK Plugins           |  |
|  +---------------------+  |
|  +---------------------+  |
|  | DDD Domain (Core)    |  |  Domain (existing)
|  | Characters, Campaigns|  |
|  | Encounters, Dice     |  |
|  +---------------------+  |
|  +---------------------+  |
|  | Infrastructure       |  |  Persistence (to be replaced)
|  | SQLite + EF Core     |  |
|  | Identity             |  |
|  +---------------------+  |
+---------------------------+
```

### Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **React/Next.js SPA** | UI rendering, game display, auth flows, character sheet, streaming text display | API Layer via REST + SignalR |
| **ASP.NET Core API** | HTTP endpoints, SignalR hub, request routing, auth middleware, CORS | Application Layer, Identity |
| **GameSessionService** | Game session lifecycle (create, load, save, resume), scoping Kernel instances per session, bridging player input to agent, streaming agent output back | SemanticKernel Agent, Repositories |
| **AuthService / Identity** | User registration, login, JWT/cookie tokens, user-to-session ownership | EF Core / SQLite |
| **SemanticKernel Agent** | LLM orchestration, tool calling, chat history management, history summarization | SK Plugins, LLM provider (Azure OpenAI) |
| **SK Plugins** | Typed wrappers exposing domain operations as LLM-callable tools | DDD Domain services + repositories |
| **DDD Domain (Core)** | Game rules, state machines, character creation, combat, campaigns, miseries calendar | Nothing (innermost layer, no outward deps) |
| **Infrastructure / Persistence** | SQLite via EF Core, repository implementations, serialization of domain aggregates | SQLite database file |

### Data Flow

**Player sends a message (main gameplay loop):**

```
1. Player types "I attack the troll with my sword"
   |
2. React SPA sends message via SignalR to GameHub
   |
3. GameHub.SendMessage(sessionId, message)
   |
4. GameSessionService looks up the session's Kernel + Agent + ChatHistory
   |
5. Agent.InvokeStreamingAsync(userMessage, agentThread)
   |  |- LLM decides to call AttackAdversary tool
   |  |- SK Plugin calls EncounterService.AttackAdversary (domain)
   |  |- Domain returns AttackOutcome (hit/miss, damage, fumble, etc.)
   |  |- LLM receives tool result, generates narrative response
   |  |- Streaming tokens flow back as IAsyncEnumerable<StreamingChatMessageContent>
   |
6. GameSessionService yields tokens to GameHub
   |
7. GameHub streams tokens to client via SignalR streaming
   |
8. React SPA displays tokens as they arrive (typewriter effect)
   |
9. After stream completes, GameSessionService persists:
   - Updated ChatHistory (or summary)
   - Modified game state (character HP, encounter state, etc.)
```

**Session management flow:**

```
1. User logs in (POST /api/auth/login) -> JWT token returned
2. User requests game list (GET /api/games) -> list of saved sessions
3. User creates/resumes game -> session state loaded from SQLite
4. SignalR connection established with JWT auth
5. Gameplay loop (above) repeats
6. On disconnect or explicit save -> state persisted to SQLite
```

## Patterns to Follow

### Pattern 1: SignalR Hub with IAsyncEnumerable Streaming

**What:** Use SignalR server-to-client streaming via `IAsyncEnumerable<T>` to deliver LLM-generated tokens to the browser in real time. This is the primary communication channel for gameplay.

**When:** Every time the LLM Game Master generates a response.

**Why:** SignalR with `IAsyncEnumerable` is the natural fit for .NET streaming. It handles connection management, reconnection, and backpressure automatically. SSE (Server-Sent Events) is simpler but lacks bidirectional communication and the reconnection semantics that SignalR provides. For a game where the player sends messages and the GM streams back, SignalR's bidirectional channel avoids managing two separate transports.

**Confidence:** HIGH -- verified against ASP.NET Core official documentation.

**Example:**

```csharp
public class GameHub : Hub
{
    private readonly GameSessionService _sessionService;

    public GameHub(GameSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async IAsyncEnumerable<GameStreamChunk> SendMessage(
        Guid sessionId,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = Context.UserIdentifier;

        await foreach (var chunk in _sessionService.ProcessPlayerInput(
            sessionId, userId, message, cancellationToken))
        {
            yield return chunk;
        }
    }
}

public record GameStreamChunk(
    string Text,                    // Narrative token(s)
    GameStateUpdate? StateUpdate,   // Optional: character HP changed, etc.
    string? EventType               // Optional: "combat_result", "misery", etc.
);
```

### Pattern 2: Scoped Kernel per Game Session

**What:** Each active game session gets its own `Kernel` instance with its own `ChatHistory` and `ChatCompletionAgent`. Sessions are loaded from persistence on resume and their Kernel is constructed fresh. Repositories are scoped per-session to enforce tenant isolation.

**When:** When a player creates or resumes a game session.

**Why:** The current codebase uses singletons for repositories (`ConcurrentDictionary`-backed). In a multi-tenant web context, each player's game state must be isolated. SemanticKernel's `Kernel` is cheap to construct. The expensive state is the `ChatHistory` (which is serialized/deserialized from SQLite) and the domain aggregates (which are loaded per-session from the database).

**Confidence:** HIGH -- based on codebase analysis and SemanticKernel DI documentation.

**Example:**

```csharp
public class GameSessionService
{
    private readonly ConcurrentDictionary<Guid, ActiveSession> _sessions = new();

    public async Task<ActiveSession> LoadOrCreateSession(
        Guid sessionId, string userId)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
            return existing;

        // Load from SQLite
        var sessionData = await _repository.GetSession(sessionId);
        var kernel = BuildKernelForSession(sessionData);
        var agent = BuildAgentForSession(kernel);
        var thread = RestoreThread(sessionData.ChatHistory);

        var session = new ActiveSession(sessionId, userId, kernel, agent, thread);
        _sessions.TryAdd(sessionId, session);
        return session;
    }
}
```

### Pattern 3: Application Service Layer (New)

**What:** Introduce a thin application service layer between the API/Hub and the domain. This layer handles cross-cutting concerns: session scoping, user authorization (does this user own this session?), state persistence coordination, and bridging the SemanticKernel agent to the web transport.

**When:** Every API request that touches game state.

**Why:** The existing Plugins talk directly to repositories. This works in a single-user console app but breaks in multi-tenant web. The application layer ensures: (a) the correct session's repositories are used, (b) state is persisted after each turn, (c) streaming is properly bridged from SK to SignalR.

**Confidence:** HIGH -- standard DDD application layer pattern.

### Pattern 4: ASP.NET Core Identity API Endpoints for Auth

**What:** Use `AddIdentityApiEndpoints<TUser>()` with `MapIdentityApi<TUser>()` to get pre-built `/register`, `/login`, `/refresh`, `/forgotPassword`, `/resetPassword` endpoints. Use bearer tokens (not cookies) for SPA authentication.

**When:** Setting up the authentication system.

**Why:** ASP.NET Core 8+ provides these API endpoints out of the box, eliminating the need to write registration/login/token-refresh logic manually. They work with EF Core and SQLite. For a SPA, bearer tokens are the cleaner approach -- the React frontend stores tokens and sends them in Authorization headers. SignalR authenticates via the access token passed as a query parameter on connection.

**Confidence:** HIGH -- verified against ASP.NET Core official Identity API docs.

**Example:**

```csharp
// Program.cs
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
    .AddEntityFrameworkStores<AppDbContext>();
builder.Services.AddAuthorization();

var app = builder.Build();
app.MapIdentityApi<IdentityUser>();
// ... other endpoints
```

### Pattern 5: Structured State Persistence with EF Core + SQLite

**What:** Persist game sessions as a combination of: (a) serialized domain aggregates (Character, Campaign, Encounter as JSON columns or normalized tables), (b) serialized ChatHistory (as JSON), and (c) session metadata (userId, timestamps, session name).

**When:** After each game turn completes and on explicit save/session close.

**Why:** SQLite is the chosen database for simple deployment. EF Core provides migrations and structured access. Domain aggregates can be serialized as JSON columns (`ToJson()` in EF Core 7+) to avoid complex relational mapping of the rich domain model, or mapped traditionally. JSON serialization is simpler for the initial implementation and preserves the domain model's shape.

**Confidence:** MEDIUM -- JSON columns in EF Core with SQLite need verification for complex nested objects. May require custom value converters.

### Pattern 6: Separate REST Endpoints for Non-Streaming Operations

**What:** Use standard REST endpoints for operations that don't require streaming: listing games, creating new games, loading character sheets, viewing game history, user profile management.

**When:** Any read or write that doesn't involve the LLM generating a narrative response.

**Why:** Not everything needs SignalR. REST is simpler for CRUD operations and works better with standard HTTP caching, error handling, and tooling. The SignalR hub should only handle the gameplay loop (player message -> GM streaming response).

**Confidence:** HIGH -- standard web API pattern.

## Anti-Patterns to Avoid

### Anti-Pattern 1: Singleton Repositories in Multi-Tenant Context

**What:** Keeping the current `ConcurrentDictionary`-backed singleton repositories when moving to web.
**Why bad:** All players would share the same in-memory state. Player A's character would be visible to Player B. No persistence across restarts. Race conditions between concurrent sessions.
**Instead:** Implement repository interfaces with EF Core + SQLite, scoped per-request or per-session. The domain's repository interfaces (`ICharactersRepository`, `ICampaignsRepository`, `IEncountersRepository`) already define the right contract -- only the implementation needs to change.

### Anti-Pattern 2: One Global Kernel / Agent Instance

**What:** Sharing a single `ChatCompletionAgent` across all connected players.
**Why bad:** Chat history would be mixed across sessions. One player's Mork Borg campaign bleeds into another's. The summarization reducer would operate on mixed content. Tool calls would affect wrong game state.
**Instead:** Each active session constructs its own Kernel, Agent, and ChatHistory. Sessions are loaded from persistence and evicted from memory when idle.

### Anti-Pattern 3: Streaming Everything Through REST with Polling

**What:** Using HTTP long-polling or repeated GET requests to check for new LLM output.
**Why bad:** Latency, wasted bandwidth, poor user experience. The "words appearing as generated" effect requires real-time streaming, not polling.
**Instead:** Use SignalR with IAsyncEnumerable for the gameplay stream. SignalR negotiates the best transport (WebSockets > SSE > Long Polling) automatically.

### Anti-Pattern 4: Persisting Raw ChatHistory Without Summarization

**What:** Storing every message in the full chat history to SQLite without summarization.
**Why bad:** Chat histories grow unbounded. Loading a resumed session would require sending the entire history to the LLM, quickly exceeding context windows and increasing cost. The existing summarization reducer is already solving this for the console app.
**Instead:** Persist the summarized history. On session resume, load the summarized history plus recent messages. The `ChatHistorySummarizationReducer` already exists in the codebase and should be used before persistence.

### Anti-Pattern 5: Exposing Domain Entities Directly Through API

**What:** Returning `Character`, `Campaign`, or `Encounter` domain objects directly from API endpoints.
**Why bad:** Couples the API contract to the domain model. Domain changes break the API. Serialization of rich domain objects (with behaviors, private setters) is fragile.
**Instead:** Use DTOs at the API boundary. The existing `CharacterDto`, `CampaignDto`, `EncounterDto` in the Semantic layer already follow this pattern and can be reused or adapted for the API layer.

## Scalability Considerations

| Concern | Single user (dev) | 10-100 users | 1000+ users |
|---------|-------------------|--------------|-------------|
| **Session memory** | All in-memory, no eviction | In-memory with LRU eviction, persist to SQLite | Consider Redis for session cache, or load-on-demand only |
| **Database** | SQLite, single file | SQLite handles concurrent reads well, use WAL mode | Migrate to PostgreSQL |
| **LLM calls** | Direct Azure OpenAI | Same, but add rate limiting per user | Queue-based, with per-user quotas |
| **SignalR connections** | Single connection | ASP.NET Core handles thousands of connections | Consider Azure SignalR Service |
| **State persistence** | Save on each turn | Same, but batch writes | Eventual consistency, async persistence |

## Component Dependency Graph (Build Order)

The following shows what depends on what, which dictates the order components should be built:

```
Level 0 (exists): DDD Domain (Core) -- no changes needed
Level 0 (exists): SemanticKernel Plugins (Semantic) -- minimal changes
Level 1 (new):    Infrastructure/SQLite -- replace in-memory repos with EF Core
Level 1 (new):    Identity/Auth -- EF Core + Identity setup
Level 2 (new):    GameSessionService -- depends on repos + SK agent
Level 3 (new):    API Layer (REST + SignalR Hub) -- depends on session service + auth
Level 4 (new):    React/Next.js Frontend -- depends on API being available
```

**Build order implications:**

1. **Phase 1: Persistence foundation** -- Replace in-memory repositories with EF Core/SQLite implementations. Add Identity for auth. This unblocks everything else.
2. **Phase 2: Application layer + API** -- Build GameSessionService, REST endpoints, and the SignalR GameHub. The agent-to-web bridge is the hardest engineering here.
3. **Phase 3: Frontend** -- Build the React SPA. Can start with mocked API responses, but full integration requires Phase 2.
4. **Phase 4: Polish** -- Session management UI, reconnection handling, mobile-responsive layout, OpenTelemetry for web.

**Critical path:** The SignalR streaming bridge between SemanticKernel's `IAsyncEnumerable<StreamingChatMessageContent>` and SignalR's `IAsyncEnumerable<T>` is the most architecturally novel piece. This should be prototyped early in Phase 2 to surface any issues with streaming + tool calling interplay.

## Key Architectural Decisions

### SignalR over Server-Sent Events (SSE)

**Decision:** Use SignalR for the gameplay stream, not raw SSE.
**Rationale:** SSE is unidirectional (server-to-client only). The game requires bidirectional communication: player sends messages AND receives streaming responses. With SSE, you'd need a separate POST endpoint for sending messages plus an SSE stream for receiving -- two transports to manage. SignalR provides both directions over a single connection, with automatic reconnection, connection management, and transport negotiation built in. SignalR can fall back to SSE or long-polling in environments where WebSockets are unavailable.

### Bearer Tokens over Cookies for SPA Auth

**Decision:** Use bearer tokens for API authentication.
**Rationale:** The frontend is a separate SPA (React/Next.js), likely served from a different origin during development. Bearer tokens avoid CORS cookie complexities. ASP.NET Core Identity API endpoints support both modes (`useCookies=false` returns bearer tokens). SignalR authenticates via the access token passed as a query string parameter during connection negotiation.

### JSON Column Serialization for Domain Aggregates

**Decision:** Start with JSON column serialization for complex domain aggregates (Character especially), with the option to normalize later.
**Rationale:** The `Character` aggregate is deeply nested (Abilities, Inventory, Armor with tiers, Scrolls, PowerPool, status flags). Mapping this to normalized relational tables is significant effort and couples persistence schema to domain model. EF Core supports `ToJson()` for owned types. JSON serialization preserves the domain shape and is faster to implement. If query performance on specific fields becomes an issue, normalize selectively.

### Existing Core Project Stays on net8.0

**Decision:** The Core project targets net8.0. The new API project should target net9.0 (matching Semantic and Infrastructure). The Core project should be upgraded to net9.0 for consistency.
**Rationale:** Mixed TFMs work but add friction. The API project needs net9.0+ for the latest ASP.NET Core features. Upgrading Core to net9.0 is a low-risk change (no API surface changes in the BCL that affect this domain).

## Sources

- ASP.NET Core SignalR Streaming: https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming (verified, official docs)
- ASP.NET Core Identity API Endpoints: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization (verified, official docs)
- SemanticKernel Chat Completion: https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion (verified, official docs)
- Existing codebase analysis: WretchedWhispers.SingleAgent.Console/Program.cs, WretchedWhispers.Semantic/*.cs, WretchedWhispers.Core/**/*.cs
