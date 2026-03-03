# Phase 3: API Layer and Streaming - Research

**Researched:** 2026-03-03
**Domain:** ASP.NET Core Minimal API, Semantic Kernel Agent Streaming, SSE, OpenTelemetry
**Confidence:** HIGH

## Summary

This phase bridges the existing Semantic Kernel ChatCompletionAgent (currently in SingleAgent.Console) into ASP.NET Core Minimal API endpoints with Server-Sent Events (SSE) for streaming LLM responses. The project runs on **.NET 9.0** (SDK 9.0.311) with Semantic Kernel 1.65.0. Since .NET 9 does not have the native `TypedResults.ServerSentEvents` API (that is a .NET 10 feature), SSE must be implemented manually via `Response.WriteAsync` with `text/event-stream` content type and explicit flushing.

The core challenge is managing Semantic Kernel's DI lifetime requirements (plugins resolved from root provider via `ImportPluginFromType<T>`) alongside ASP.NET Core's scoped-per-request pattern. The existing codebase already has two DI paths: `AddDomainServices()` (Scoped for web) and `AddSqliteInfrastructure()` (Transient for SK). The API will need a merged approach where the Kernel is built per-request using a scoped service provider, or plugins are registered via `AddKernel()` with proper lifetime alignment.

**Primary recommendation:** Build a `GameSessionService` that encapsulates per-turn agent orchestration (Kernel creation, plugin import, agent invocation, chat history persistence, state commit), and expose it through Minimal API endpoints that stream SSE events using manual `Response.WriteAsync`/`FlushAsync` on .NET 9. Use `ConcurrentDictionary<Guid, SemaphoreSlim>` for per-session concurrency control (409 Conflict on double-submit).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Single endpoint to create a session (POST). No separate character creation step -- the GM guides character creation conversationally as the first interaction
- Session maps 1:1 to Campaign (no separate session abstraction layer)
- Session list (GET) returns rich previews: session ID, character name, class, HP, campaign description snippet, last played date, session status (in-progress, character-creation, ended)
- Resuming a session returns character/campaign state + last N chat messages (paginated). Client can fetch older messages on scroll-up via a separate endpoint
- Per-turn SSE: client opens SSE connection when submitting an action, receives the streamed response, connection closes when the GM finishes. No persistent long-lived connections
- Tool calls (dice rolls, combat, state mutations) emit as separate structured SSE events alongside narrative text -- "mechanical sidebar" pattern. The client decides how to display them
- Stream emits typed events: `narrative` (text chunks), `tool_result` (mechanical outcomes), `state_update` (game state deltas like HP changes, inventory updates). Client stays in sync without re-fetching
- Single ChatCompletionAgent (GM only) for this phase. No HandoffOrchestration or multi-agent setup yet
- Free-text input only. Player types natural language, the GM interprets intent and calls the appropriate domain tools. No structured action commands
- POST /sessions/{id}/actions returns the SSE stream directly (Content-Type: text/event-stream). One request = one streamed response
- 409 Conflict returned if a GM response is already in progress for the same session. Prevents double-submit and multi-tab conflicts
- GM sends the first message automatically when a new session is created. Player's first action is a response to the GM's introduction
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

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INFR-01 | .NET API layer with SSE streaming for LLM responses | Manual SSE via Response.WriteAsync on .NET 9; SK InvokeStreamingAsync returns IAsyncEnumerable<StreamingChatMessageContent> to bridge into SSE events |
| INFR-04 | OpenTelemetry observability for API and LLM calls | OpenTelemetry.Extensions.Hosting + ASP.NET Core instrumentation + SK's built-in "Microsoft.SemanticKernel" activity source for LLM tracing |
| SESS-01 | User can create a new game session | POST /sessions endpoint creates Campaign via CampaignPlugin, associates with UserId, triggers GM first-message via agent streaming |
| SESS-02 | User can view list of their existing game sessions | GET /sessions using ICampaignsRepository.GetForUser(userId) with CampaignEntity.UserId FK for multi-tenant isolation |
| SESS-03 | User can continue a saved game session from where they left off | GET /sessions/{id} loads campaign state + paginated chat history from SqliteChatHistoryRepository |
| SESS-04 | Game state auto-saves after each player action | Transactional commit pattern: buffer all domain mutations during agent turn, persist only on successful completion |
| GAME-06 | Graceful error recovery when LLM fails or times out | Retry with exponential backoff (2-3 attempts), error SSE event on failure, no partial state commits |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.SemanticKernel | 1.65.0 | AI agent orchestration, chat completion, function calling | Already in use; provides ChatCompletionAgent with InvokeStreamingAsync |
| Microsoft.SemanticKernel.Agents.Core | 1.65.0 | ChatCompletionAgent, ChatHistoryAgentThread | Needed for agent streaming; already referenced in SingleAgent.Console |
| Microsoft.SemanticKernel.Connectors.AzureOpenAI | 1.65.0 | Azure OpenAI chat completion service | Already in use for LLM connectivity |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.x | SQLite persistence | Already in use for all game state |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 9.0.x | Identity with bearer tokens | Already configured in API project |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| OpenTelemetry.Extensions.Hosting | 1.15.x | AddOpenTelemetry() builder for ASP.NET Core | Required for INFR-04 observability |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.x | Auto-instrumentation of HTTP requests | Tracing all API endpoints |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.x | OTLP exporter (Jaeger, Aspire Dashboard, etc.) | Standard exporter for traces/metrics |
| OpenTelemetry.Exporter.Console | 1.13.x | Console exporter for development | Already in SingleAgent.Console; useful for dev |
| Microsoft.Extensions.Resilience | 9.x | Polly v8 resilience pipelines (retry, timeout) | Server-side LLM retry with exponential backoff |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Manual SSE (Response.WriteAsync) | Upgrade to .NET 10 for TypedResults.ServerSentEvents | .NET 10 provides cleaner API but requires TFM upgrade of entire solution; manual SSE on .NET 9 is well-understood and works fine |
| Polly/Microsoft.Extensions.Resilience | Custom retry loop | Polly provides jitter, circuit breaker, timeout composition; custom loop misses edge cases |
| ConcurrentDictionary<Guid, SemaphoreSlim> | Keyed semaphores library | Built-in approach is simple enough for single-server; no external dependency needed |

**Installation (new packages for API project):**
```bash
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package Microsoft.SemanticKernel --version 1.65.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package Microsoft.SemanticKernel.Agents.Core --version 1.65.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package Microsoft.SemanticKernel.Connectors.AzureOpenAI --version 1.65.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package OpenTelemetry.Extensions.Hosting --version 1.15.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package OpenTelemetry.Instrumentation.AspNetCore --version 1.15.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.15.0
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package OpenTelemetry.Exporter.Console --version 1.13.1
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj package Microsoft.Extensions.Resilience
```

**Project reference (API must reference Semantic project):**
```bash
dotnet add WrtechedWhispers/WretchedWhispers.Api/WretchedWhispers.Api.csproj reference WrtechedWhispers/WretchedWhispers.Semantic/WretchedWhispers.Semantic.csproj
```

## Architecture Patterns

### Recommended Project Structure
```
WretchedWhispers.Api/
  Program.cs                          # DI setup, middleware, endpoint mapping
  Endpoints/
    SessionEndpoints.cs               # MapGroup("/sessions") with all session routes
  Services/
    GameSessionService.cs             # Orchestrates agent per-turn (Kernel, agent, streaming, commit)
    SessionConcurrencyGuard.cs        # ConcurrentDictionary<Guid, SemaphoreSlim> for 409 Conflict
  Models/
    CreateSessionResponse.cs          # API response DTOs
    SessionPreviewDto.cs              # Session list preview model
    SessionDetailDto.cs               # Resume session response
    SseEventPayloads.cs               # Typed SSE event payload models
  Configuration/
    OpenTelemetryConfiguration.cs     # OTel setup extension methods
    SemanticKernelConfiguration.cs    # SK DI registration for API context
```

### Pattern 1: Manual SSE Streaming on .NET 9
**What:** Write SSE events directly to HttpResponse using the SSE wire format
**When to use:** .NET 9 (no TypedResults.ServerSentEvents available)
**Example:**
```csharp
// Source: https://www.petkir.at/blog/semantic-kernel/01_chat_03_sse + ASP.NET Core docs
app.MapPost("/sessions/{sessionId}/actions", async (
    Guid sessionId,
    PlayerActionRequest request,
    GameSessionService gameService,
    SessionConcurrencyGuard guard,
    HttpContext http,
    CancellationToken ct) =>
{
    if (!await guard.TryAcquire(sessionId))
        return Results.Conflict(new { error = "GM response already in progress" });

    try
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.Connection = "keep-alive";

        await foreach (var sseEvent in gameService.ProcessAction(sessionId, request.Message, ct))
        {
            await http.Response.WriteAsync($"event: {sseEvent.EventType}\n", ct);
            await http.Response.WriteAsync($"data: {sseEvent.JsonData}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }

        // Signal stream completion
        await http.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
        return Results.Empty;
    }
    catch (Exception)
    {
        // Error event if stream hasn't started
        await http.Response.WriteAsync("event: error\ndata: {\"message\":\"An unexpected error occurred\"}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
        return Results.Empty;
    }
    finally
    {
        guard.Release(sessionId);
    }
}).RequireAuthorization();
```

### Pattern 2: Semantic Kernel Agent Streaming Bridge
**What:** Bridge SK's `InvokeStreamingAsync` IAsyncEnumerable into typed SSE events
**When to use:** Converting SK streaming output to structured SSE for the client
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

public async IAsyncEnumerable<SseEvent> ProcessAction(
    Guid sessionId,
    string playerMessage,
    [EnumeratorCancellation] CancellationToken ct)
{
    // Build kernel per-request with scoped services
    var kernel = BuildKernelForSession(sessionId);
    var agent = CreateGameMasterAgent(kernel);
    var thread = await LoadOrCreateThread(sessionId, ct);

    var message = new ChatMessageContent(AuthorRole.User, playerMessage);

    // Buffer domain mutations for transactional commit
    var mutationBuffer = new List<ChatMessageContent>();

    await foreach (var chunk in agent.InvokeStreamingAsync(message, thread).WithCancellation(ct))
    {
        if (!string.IsNullOrEmpty(chunk.Content))
        {
            yield return new SseEvent("narrative", new { text = chunk.Content });
        }
    }

    // After full response: read completed messages from thread for tool results
    await foreach (var completed in thread.GetMessagesAsync().WithCancellation(ct))
    {
        foreach (var item in completed.Items)
        {
            if (item is FunctionResultContent funcResult)
            {
                yield return new SseEvent("tool_result", new
                {
                    function = funcResult.FunctionName,
                    result = funcResult.Result
                });
            }
        }
    }

    // Commit all domain state changes to DB (transactional)
    await CommitSessionState(sessionId, thread, ct);

    // Emit state_update with current game state delta
    var stateSnapshot = await GetStateSnapshot(sessionId, ct);
    yield return new SseEvent("state_update", stateSnapshot);
}
```

### Pattern 3: Per-Request Kernel with Scoped Services
**What:** Create a Kernel per HTTP request that resolves scoped services for plugins
**When to use:** API endpoints where DbContext and repos must be scoped per request
**Example:**
```csharp
// Source: https://devblogs.microsoft.com/semantic-kernel/using-semantic-kernel-with-dependency-injection/
// Source: Existing SingleAgent.Console/Program.cs pattern

// In DI registration:
builder.Services.AddScoped<GameSessionService>();

// GameSessionService builds kernel from scoped IServiceProvider
public class GameSessionService(
    IServiceProvider serviceProvider,
    IConfiguration configuration)
{
    private Kernel BuildKernelForSession(Guid sessionId)
    {
        var kernelBuilder = Kernel.CreateBuilder();

        // Use the request-scoped service provider
        kernelBuilder.Services.AddSingleton(serviceProvider);

        var azureSettings = configuration.GetSection("AzureOpenAi");
        kernelBuilder.AddAzureOpenAIChatCompletion(
            azureSettings["ChatModelDeployment"]!,
            azureSettings["Endpoint"]!,
            azureSettings["ApiKey"]!);

        var kernel = kernelBuilder.Build();

        // Import plugins -- these resolve dependencies from the scoped provider
        kernel.ImportPluginFromType<CharacterPlugin>("Character");
        kernel.ImportPluginFromType<CampaignPlugin>("Campaign");
        kernel.ImportPluginFromType<EncounterPlugin>("Encounter");
        kernel.ImportPluginFromType<DicePlugin>("Dice");

        return kernel;
    }
}
```

**CRITICAL DI INSIGHT:** `ImportPluginFromType<T>()` creates a new instance of T using the Kernel's internal service provider. In the console app, this uses `AddSqliteInfrastructure` (Transient DbContext resolved from root). For the API, the Kernel must be constructed with access to the request-scoped `IServiceProvider` so that scoped DbContext and repositories are used. The recommended approach: construct the Kernel within a Scoped service, passing the scoped IServiceProvider. Alternatively, use `AddFromObject()` to import pre-resolved plugin instances.

### Pattern 4: Transactional State Buffering
**What:** Buffer all domain mutations during an agent turn, commit only on success
**When to use:** GAME-06 error recovery requirement
**Implementation approach:**
```csharp
// The key insight: SK plugins currently write directly to DB via repositories.
// For transactional behavior, we have two options:
//
// Option A (Recommended): Use EF Core's change tracker as the buffer.
//   - Scoped DbContext accumulates changes during the request
//   - Call SaveChangesAsync() only after full agent response completes
//   - On error, simply dispose the scope (changes never persisted)
//   - Requires plugins to NOT call SaveChangesAsync individually
//
// Option B: Wrap the entire turn in a DB transaction.
//   - Begin transaction before agent invoke
//   - Commit after successful completion
//   - Rollback on error
//
// Option A is cleaner but requires refactoring plugin repositories to not auto-save.
// Option B works with current code but adds transaction management complexity.
// Recommend Option B for minimal changes to existing plugin code.
```

### Pattern 5: Session Concurrency Guard
**What:** Prevent concurrent GM responses for the same session
**When to use:** 409 Conflict requirement for double-submit prevention
**Example:**
```csharp
public class SessionConcurrencyGuard
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<bool> TryAcquire(Guid sessionId)
    {
        var semaphore = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(TimeSpan.Zero);
    }

    public void Release(Guid sessionId)
    {
        if (_locks.TryGetValue(sessionId, out var semaphore))
            semaphore.Release();
    }
}
// Register as Singleton
```

### Anti-Patterns to Avoid
- **Singleton Kernel in API:** Kernels are mutable and store per-invocation state. Always create per-request or per-turn. Official docs explicitly state "Kernels are typically registered as transient."
- **Long-lived SSE connections:** The decision specifies per-turn SSE (open connection, stream response, close). Do NOT keep a persistent SSE connection per session.
- **Committing state mid-stream:** If the LLM fails partway through a response, partial state changes corrupt game state. Buffer all mutations, commit only after full completion.
- **Resolving scoped services from root provider:** SK's `ImportPluginFromType<T>()` creates instances from the Kernel's service provider. If that's the root provider, scoped services (like DbContext) throw or create new instances that bypass the request scope.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSE wire format | Custom event framing library | Standard `data: ...\n\nevent: ...\n` format with Response.WriteAsync | SSE spec is simple; the format is just `event:`, `data:`, `id:` fields with double newline terminator |
| LLM retry logic | Custom retry loops with Thread.Sleep | Microsoft.Extensions.Resilience (Polly v8) resilience pipeline | Polly handles jitter, backoff, timeout composition, and integrates with .NET DI |
| Chat history persistence | Custom message serialization | Existing SqliteChatHistoryRepository | Already handles FunctionCallContent serialization, ordering, session management |
| Chat history summarization | Manual context window trimming | Existing ChatHistorySummarizationReducer from SingleAgent.Console | Already configured with Mork Borg-specific summarization instructions |
| Authentication | Custom JWT validation | Existing Identity API endpoints + RequireAuthorization() | Already configured with bearer tokens (60min access, 14-day refresh) |
| Multi-tenant session isolation | Custom query filters | CampaignEntity.UserId FK + ICampaignsRepository.GetForUser(userId) | Already implemented with indexed FK in CampaignEntityConfiguration |
| OpenTelemetry for SK calls | Custom logging around LLM calls | SK's built-in "Microsoft.SemanticKernel" ActivitySource | SK already emits traces for all kernel function executions and AI model calls |

**Key insight:** The existing codebase already has most of the building blocks. This phase is primarily about wiring them together through an HTTP layer, not building new domain capabilities.

## Common Pitfalls

### Pitfall 1: DbContext Lifetime Mismatch with SK Plugins
**What goes wrong:** `ImportPluginFromType<T>()` resolves dependencies from the Kernel's service provider. If the Kernel is built with a root/singleton provider, scoped DbContext instances are either shared across requests or throw "cannot resolve scoped service from root provider."
**Why it happens:** The console app uses Transient DbContext (via `AddSqliteInfrastructure`) which works from any provider. The API uses Scoped DbContext (standard for ASP.NET Core). Mixing these two DI paths without care breaks things.
**How to avoid:** Build the Kernel within a Scoped service (e.g., GameSessionService) and pass the request-scoped IServiceProvider. Or use `kernel.ImportPluginFromObject()` to import pre-resolved plugin instances from the scoped container.
**Warning signs:** "Cannot resolve scoped service from root provider" exception, or silent data loss from multiple DbContext instances not sharing the same transaction.

### Pitfall 2: Forgetting FlushAsync After WriteAsync
**What goes wrong:** SSE events are buffered and not delivered to the client until the buffer fills or the response completes.
**Why it happens:** ASP.NET Core buffers response writes by default for performance.
**How to avoid:** Always call `await Response.Body.FlushAsync()` after each SSE event write.
**Warning signs:** Client receives all events at once when the stream ends, rather than incrementally.

### Pitfall 3: SK Streaming Tool Call Visibility
**What goes wrong:** `InvokeStreamingAsync` yields `StreamingChatMessageContent` chunks that contain narrative text, but intermediate tool call results may not be visible in the streamed output.
**Why it happens:** Known SK behavior -- streaming content focuses on text chunks. Tool calls execute internally, and their results are added to the ChatHistory (accessible via `thread.GetMessagesAsync()`) after the full response.
**How to avoid:** After the streaming loop completes, read `thread.GetMessagesAsync()` to extract FunctionCallContent and FunctionResultContent from completed messages. Emit these as `tool_result` SSE events after (or alongside) narrative events.
**Warning signs:** Client receives narrative text but no dice roll / combat / state mutation events.

### Pitfall 4: Missing CancellationToken Propagation
**What goes wrong:** Client disconnects (closes browser tab, network drops) but server continues processing the LLM response, wasting resources and potentially corrupting state.
**Why it happens:** Forgetting to pass `HttpContext.RequestAborted` (or the endpoint's CancellationToken) through to the agent invocation.
**How to avoid:** Accept `CancellationToken ct` in the endpoint handler (ASP.NET Core binds `HttpContext.RequestAborted` automatically) and pass it through to `InvokeStreamingAsync`, all DB operations, and the streaming loop.
**Warning signs:** Server logs showing completed LLM responses after client disconnected.

### Pitfall 5: Transactional Commit with Existing Plugin Code
**What goes wrong:** Current SK plugins (CampaignPlugin, CharacterPlugin, etc.) call `repository.Save*()` internally, which calls `SaveChangesAsync()`. This means domain mutations are committed immediately during the agent turn, before the full response completes.
**Why it happens:** The plugins were designed for the console app where there's no need for transactional behavior across an entire turn.
**How to avoid:** Two approaches: (1) Wrap the entire agent turn in a database transaction (`BeginTransactionAsync` / `CommitAsync` / `RollbackAsync`), so individual `SaveChangesAsync` calls write to the transaction which is only committed at the end. (2) Refactor plugins to defer saves. Approach (1) is recommended for minimal code changes.
**Warning signs:** After an LLM failure mid-response, the character has fewer HP or items are missing -- state was partially committed.

### Pitfall 6: SSE Response After Results.Conflict
**What goes wrong:** Returning `Results.Conflict()` after already writing to `Response` causes a "Headers already sent" exception.
**Why it happens:** SSE endpoints write directly to HttpResponse, bypassing the normal IResult pipeline. The 409 check must happen BEFORE any writes to Response.
**How to avoid:** Check concurrency guard and return 409 before setting `Response.ContentType` or writing any bytes. Once the SSE response has started, errors must be communicated as SSE error events, not HTTP status codes.
**Warning signs:** "Headers are read-only, response has already started" exceptions.

## Code Examples

### SSE Event Model
```csharp
// Typed SSE event for structured streaming
public record SseEvent(string EventType, object Data)
{
    public string JsonData => JsonSerializer.Serialize(Data, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}

// Event types matching the locked decision:
// "narrative"    -> { "text": "The dungeon reeks of..." }
// "tool_result"  -> { "function": "ChallengeCharacter", "result": { "isSuccess": true } }
// "state_update" -> { "hp": 6, "maxHp": 8, "silver": 12, ... }
// "error"        -> { "message": "LLM service unavailable" }
// "done"         -> {}
```

### OpenTelemetry Configuration
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
// Source: https://learn.microsoft.com/en-us/semantic-kernel/concepts/enterprise-readiness/observability/

// Enable SK diagnostic telemetry (must be before any SK usage)
AppContext.SetSwitch(
    "Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("WretchedWhispers.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource("Microsoft.SemanticKernel*")  // SK's built-in activity sources
        .AddOtlpExporter()                        // OTLP for production
        .AddConsoleExporter())                     // Console for development
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("Microsoft.SemanticKernel*")
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
});
```

### Session List Endpoint (Rich Preview)
```csharp
// Source: Existing ICampaignsRepository.GetForUser + CampaignEntity.UserId pattern
app.MapGet("/sessions", async (
    HttpContext http,
    ICampaignsRepository campaignsRepo,
    ICharactersRepository charactersRepo,
    IChatHistoryRepository chatRepo) =>
{
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var campaigns = await campaignsRepo.GetForUser(userId);

    var previews = new List<SessionPreviewDto>();
    foreach (var campaign in campaigns)
    {
        // Load character for preview (first character in campaign)
        // Load last chat session for "last played" timestamp
        previews.Add(new SessionPreviewDto
        {
            SessionId = campaign.Id,
            CampaignName = campaign.Name,
            Description = campaign.Description,
            // ... character name, HP, status, lastPlayed
        });
    }

    return Results.Ok(previews);
}).RequireAuthorization();
```

### Resilience Pipeline for LLM Retry
```csharp
// Source: https://www.pollydocs.org/strategies/retry.html
// Using Microsoft.Extensions.Resilience (Polly v8)

builder.Services.AddResiliencePipeline("llm-retry", pipelineBuilder =>
{
    pipelineBuilder
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,                          // 2-3 total attempts
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
        })
        .AddTimeout(TimeSpan.FromMinutes(2));              // Overall timeout
});
```

### GM Agent Construction (Reuse from Console)
```csharp
// Source: Existing SingleAgent.Console/Program.cs (lines 119-163)
// The agent definition, instructions, and summarization reducer should be
// extracted to a shared location and reused in both Console and API.

private ChatCompletionAgent CreateGameMasterAgent(Kernel kernel)
{
    var chatService = kernel.GetRequiredService<IChatCompletionService>();
    var summarizer = new ChatHistorySummarizationReducer(
        chatService, targetCount: 100, thresholdCount: 150)
    {
        SummarizationInstructions = GmPrompts.SummarizationInstructions
    };

    return new ChatCompletionAgent
    {
        Name = "Game_Master",
        HistoryReducer = summarizer,
        Instructions = GmPrompts.SystemInstructions,
        Kernel = kernel,
        Arguments = new KernelArguments(
            new AzureOpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
    };
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `kernel.InvokeAsync` (blocking) | `agent.InvokeStreamingAsync` (streaming) | SK 1.x Agents | Enables token-by-token delivery over SSE |
| Manual SSE formatting | `TypedResults.ServerSentEvents<T>` | .NET 10 (Preview 2025) | NOT available on .NET 9 -- must use manual approach |
| Polly v7 policies | Polly v8 resilience pipelines via Microsoft.Extensions.Resilience | 2024 | New builder API; integrates with .NET DI |
| `AddOpenTelemetry` per-signal | `UseOtlpExporter()` cross-cutting | OTel 1.8.0-beta.1 | Single extension for all signals; still beta but usable |
| SK `Kernel.CreateBuilder()` standalone | `services.AddKernel()` DI integration | SK 1.x stable | Proper DI lifecycle management |

**Deprecated/outdated:**
- SK `ChatHistoryAgentThread` -- this is the current approach and is NOT deprecated, but the streaming behavior for tool calls has known gaps (see Pitfall 3). Thread is updated with full messages after streaming completes.
- The `SKEXP0001`/`SKEXP0110` pragma warnings are still needed for Agent APIs (experimental status in SK 1.65.0).

## Open Questions

1. **SK Plugin Service Resolution in Scoped Context**
   - What we know: `ImportPluginFromType<T>()` uses `ActivatorUtilities.CreateInstance` from the Kernel's service provider. The console app uses Transient services from root provider. The API needs scoped services.
   - What's unclear: Whether passing a scoped `IServiceProvider` to the Kernel builder correctly scopes plugin dependency resolution for the entire request lifecycle.
   - Recommendation: Test early with a spike. If `ImportPluginFromType` doesn't honor scoped lifetimes, fall back to `ImportPluginFromObject()` with pre-resolved instances from the request scope.

2. **Streaming Tool Call Event Timing**
   - What we know: `InvokeStreamingAsync` streams text content. Tool call results are available via `thread.GetMessagesAsync()` after completion.
   - What's unclear: Whether tool results are available _during_ streaming (interleaved with text) or only after the full response. SK issue #13047 was closed without clear resolution.
   - Recommendation: Implement the `tool_result` events post-stream initially. If real-time tool visibility is needed, explore SK's filter/event hooks or check `StreamingChatMessageContent.Items` for `FunctionResultContent` during the streaming loop.

3. **Chat History Page Size for Resume**
   - What we know: CONTEXT.md specifies paginated last-N messages on resume, with scroll-up for older.
   - Recommendation: Default to 50 messages per page. This balances context (enough to see recent conversation) with response size. Expose as query parameter `?page=1&pageSize=50`.

4. **GM Response Timeout**
   - What we know: LLM responses with tool calls can take 30-120 seconds depending on complexity.
   - Recommendation: 3-minute overall timeout (covers multi-tool-call turns). Configurable via `appsettings.json`. Polly timeout strategy wraps the entire agent invocation.

## Discretion Recommendations

Based on the "Claude's Discretion" areas from CONTEXT.md:

| Area | Recommendation | Rationale |
|------|---------------|-----------|
| GM response timeout | 180 seconds (3 minutes), configurable in appsettings.json | Multi-tool-call turns (create character, create campaign, start) can chain 4-5 LLM roundtrips |
| SSE event format | `event: {type}\ndata: {json}\n\n` with camelCase JSON | Standard SSE wire format; camelCase matches JS conventions |
| Chat history page size | 50 messages per page, `?page=1&pageSize=50` | Balances context with payload size; most sessions < 100 messages |
| Retry backoff | Exponential with jitter, 2 retries, 1s base delay | Polly v8 standard; jitter prevents thundering herd |
| SK Kernel lifecycle | Scoped GameSessionService builds Kernel per-turn | Kernel is mutable/transient by design; scoped service ensures request-scoped DbContext alignment |

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn - Agent Streaming](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-streaming) - InvokeStreamingAsync API, StreamingChatMessageContent, ChatHistoryAgentThread
- [Microsoft Learn - SK Observability](https://learn.microsoft.com/en-us/semantic-kernel/concepts/enterprise-readiness/observability/) - OpenTelemetry integration, ActivitySource names, metric names
- [Microsoft Learn - Minimal API Responses (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0) - TypedResults.ServerSentEvents (confirmed .NET 10 only)
- [SK DI Blog Post](https://devblogs.microsoft.com/semantic-kernel/using-semantic-kernel-with-dependency-injection/) - AddKernel(), transient Kernel, singleton services pattern
- [SK Source - AddKernel](https://github.com/microsoft/semantic-kernel/blob/main/dotnet/src/SemanticKernel.Abstractions/Services/KernelServiceCollectionExtensions.cs) - Kernel registered as transient
- [OpenTelemetry .NET Docs](https://opentelemetry.io/docs/languages/dotnet/exporters/) - OTLP exporter configuration
- [Polly v8 Retry Strategy](https://www.pollydocs.org/strategies/retry.html) - Exponential backoff with jitter

### Secondary (MEDIUM confidence)
- [petkir.at - SK + SSE in ASP.NET Core](https://www.petkir.at/blog/semantic-kernel/01_chat_03_sse) - Practical SK-to-SSE bridge pattern
- [Milan Jovanovic - SSE in ASP.NET Core and .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) - SSE patterns, confirmed .NET 10 native support
- [SK Issue #13047](https://github.com/microsoft/semantic-kernel/issues/13047) - Streaming tool call results gap (closed/stale)
- [Microsoft Learn - .NET Observability with OTel](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) - AddOpenTelemetry builder pattern

### Tertiary (LOW confidence)
- None -- all findings verified against official sources.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All packages already in use or well-documented Microsoft packages
- Architecture: HIGH - Patterns derived from existing codebase + official SK/ASP.NET Core docs
- Pitfalls: HIGH - Multiple sources confirm DI lifetime issues, SSE flush requirements, and SK streaming tool call gaps
- SSE on .NET 9 specifics: MEDIUM - Manual pattern is well-known but less documented than .NET 10 native approach

**Research date:** 2026-03-03
**Valid until:** 2026-04-03 (stable; SK 1.65.0 and .NET 9.0 are current)
