# TurnCoordinator Seams Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the transaction boundary (`IUnitOfWork`) and the streaming plumbing (`AsyncStreamBridge`) out of `TurnCoordinator` so it keeps only the orchestration narrative and no longer touches EF or channels.

**Architecture:** Two new seams. (1) `IUnitOfWork`/`IUnitOfWorkScope` in `Infrastructure.Persistence`, implemented by `EfUnitOfWork` wrapping the scoped `WretchedWhispersDbContext`'s transaction; commit propagates `DbUpdateConcurrencyException`, dispose-without-commit rolls back best-effort. (2) `AsyncStreamBridge.Run<T>` in `Api.Services`, a generic channel→IAsyncEnumerable helper. `TurnCoordinator` is rewritten to depend on `IUnitOfWork` instead of `WretchedWhispersDbContext` and to return `AsyncStreamBridge.Run(...)` instead of hand-rolling a channel.

**Tech Stack:** C# / .NET 10, EF Core (SQLite), `System.Threading.Channels`, xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-06-09-turn-coordinator-seams-design.md`

**Dependency direction (must respect):** Api → Infrastructure → Core. The UoW interface lives in Infrastructure because its implementation does. `AsyncStreamBridge` is Api-only.

---

## Task 1: `AsyncStreamBridge` (streaming plumbing)

Generic helper that runs a producer delegate against a channel and yields the results, owning the "no `yield` in `try/catch`" workaround and guaranteed channel completion.

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Api/Services/AsyncStreamBridge.cs`
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Services/AsyncStreamBridgeTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `WrtechedWhispers/WretchedWhispers.Tests/Services/AsyncStreamBridgeTests.cs`:

```csharp
using System.Threading.Channels;
using WretchedWhispers.Api.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class AsyncStreamBridgeTests
{
    [Fact]
    public async Task Run_YieldsItemsWrittenByProducer_InOrder_ThenCompletes()
    {
        var stream = AsyncStreamBridge.Run<int>(async (writer, _) =>
        {
            writer.TryWrite(1);
            writer.TryWrite(2);
            writer.TryWrite(3);
            await Task.CompletedTask;
        }, CancellationToken.None);

        var items = new List<int>();
        await foreach (var i in stream)
            items.Add(i);

        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public async Task Run_WhenProducerThrows_SurfacesExceptionToConsumer_WithoutHanging()
    {
        var stream = AsyncStreamBridge.Run<int>((writer, _) =>
        {
            writer.TryWrite(1);
            throw new InvalidOperationException("boom");
        }, CancellationToken.None);

        var items = new List<int>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var i in stream)
                items.Add(i);
        });

        Assert.Equal("boom", ex.Message);
        Assert.Equal(new[] { 1 }, items); // items written before the throw still drain
    }

    [Fact]
    public async Task Run_WhenTokenAlreadyCancelled_StopsConsumer()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var stream = AsyncStreamBridge.Run<int>(async (writer, _) =>
        {
            writer.TryWrite(1);
            await Task.CompletedTask;
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in stream)
            {
            }
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --filter "FullyQualifiedName~AsyncStreamBridgeTests"`
Expected: FAIL — build error, `AsyncStreamBridge` does not exist.

- [ ] **Step 3: Write the implementation**

Create `WrtechedWhispers/WretchedWhispers.Api/Services/AsyncStreamBridge.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Bridges a producer delegate to an <see cref="IAsyncEnumerable{T}"/>. Exists to work around C#'s
/// rule that <c>yield return</c> cannot appear inside a <c>try/catch</c>: the producer writes events
/// into a channel (and may wrap its body in try/catch), while this method reads from the channel and
/// yields outside any try/catch. Domain-agnostic — it knows nothing about game events.
///
/// The producer is fire-and-forget; the channel is always completed (in a finally), and if the
/// producer throws, the channel is completed with that exception so the consumer rethrows it rather
/// than hanging.
/// </summary>
public static class AsyncStreamBridge
{
    public static async IAsyncEnumerable<T> Run<T>(
        Func<ChannelWriter<T>, CancellationToken, Task> produce,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceAndCompleteAsync(produce, channel.Writer, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    private static async Task ProduceAndCompleteAsync<T>(
        Func<ChannelWriter<T>, CancellationToken, Task> produce,
        ChannelWriter<T> writer,
        CancellationToken ct)
    {
        try
        {
            await produce(writer, ct);
            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --filter "FullyQualifiedName~AsyncStreamBridgeTests"`
Expected: PASS — 3 passed.

- [ ] **Step 5: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/AsyncStreamBridge.cs WrtechedWhispers/WretchedWhispers.Tests/Services/AsyncStreamBridgeTests.cs
git commit -m "feat: add AsyncStreamBridge channel-to-IAsyncEnumerable helper"
```

---

## Task 2: `IUnitOfWork` seam (transaction boundary)

Interface + EF implementation + DI registration. Wraps the scoped `DbContext` transaction so the coordinator never touches EF.

**Files:**
- Create: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/IUnitOfWork.cs`
- Create: `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/EfUnitOfWork.cs`
- Modify: `WrtechedWhispers/WretchedWhispers.Infrastructure/ServiceCollectionExtensions.cs` (register in `AddDomainServices`)
- Test: `WrtechedWhispers/WretchedWhispers.Tests/Persistence/EfUnitOfWorkTests.cs`

- [ ] **Step 1: Write the interface**

Create `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/IUnitOfWork.cs`:

```csharp
namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// Abstracts the per-turn atomic boundary so callers (e.g. TurnCoordinator) never depend on EF
/// directly. The implementation MUST wrap the same request-scoped DbContext that the turn's tools
/// mutate through — that shared scope is what places those writes inside this transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct);
}

/// <summary>
/// An open transactional scope. <see cref="CommitAsync"/> commits and lets a
/// <c>DbUpdateConcurrencyException</c> propagate to the caller (a UX/policy decision belongs in the
/// caller, not here). Disposal without a prior commit rolls back, best-effort.
/// </summary>
public interface IUnitOfWorkScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing tests**

Create `WrtechedWhispers/WretchedWhispers.Tests/Persistence/EfUnitOfWorkTests.cs`:

```csharp
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Persistence;

public class EfUnitOfWorkTests : SqliteTestBase
{
    [Fact]
    public async Task Commit_PersistsChanges()
    {
        var id = Guid.NewGuid();
        var uow = new EfUnitOfWork(Db);

        await using (var scope = await uow.BeginAsync(CancellationToken.None))
        {
            Db.Campaigns.Add(new CampaignEntity { Id = id, Data = "x", UserId = "u", Version = Guid.NewGuid() });
            await Db.SaveChangesAsync();
            await scope.CommitAsync(CancellationToken.None);
        }

        using var other = CreateSeparateContext();
        Assert.NotNull(await other.Campaigns.FindAsync(id));
    }

    [Fact]
    public async Task DisposeWithoutCommit_RollsBack()
    {
        var id = Guid.NewGuid();
        var uow = new EfUnitOfWork(Db);

        await using (var scope = await uow.BeginAsync(CancellationToken.None))
        {
            Db.Campaigns.Add(new CampaignEntity { Id = id, Data = "x", UserId = "u", Version = Guid.NewGuid() });
            await Db.SaveChangesAsync();
            // no CommitAsync — disposal must roll back
        }

        using var other = CreateSeparateContext();
        Assert.Null(await other.Campaigns.FindAsync(id));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --filter "FullyQualifiedName~EfUnitOfWorkTests"`
Expected: FAIL — build error, `EfUnitOfWork` does not exist.

- [ ] **Step 4: Write the implementation**

Create `WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/EfUnitOfWork.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace WretchedWhispers.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. Wraps a transaction on the injected
/// (request-scoped) <see cref="WretchedWhispersDbContext"/>, so every repository write made on that
/// same scope during the turn is part of the same transaction.
/// </summary>
public sealed class EfUnitOfWork(WretchedWhispersDbContext dbContext) : IUnitOfWork
{
    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        return new Scope(transaction);
    }

    private sealed class Scope(IDbContextTransaction transaction) : IUnitOfWorkScope
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken ct)
        {
            await transaction.CommitAsync(ct);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_committed)
                    await transaction.RollbackAsync();
            }
            catch
            {
                // Best-effort: rollback may fail if the connection is already closed.
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --filter "FullyQualifiedName~EfUnitOfWorkTests"`
Expected: PASS — 2 passed.

- [ ] **Step 6: Register in DI**

In `WrtechedWhispers/WretchedWhispers.Infrastructure/ServiceCollectionExtensions.cs`, inside `AddDomainServices`, add the registration next to the repositories. Find:

```csharp
        services.AddScoped<IChatHistoryRepository, SqliteChatHistoryRepository>();
```

Add immediately after it:

```csharp
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
```

- [ ] **Step 7: Build to verify DI compiles**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Expected: `0 errors` (8 pre-existing NU1902 OpenTelemetry warnings are unrelated).

- [ ] **Step 8: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/IUnitOfWork.cs WrtechedWhispers/WretchedWhispers.Infrastructure/Persistence/EfUnitOfWork.cs WrtechedWhispers/WretchedWhispers.Infrastructure/ServiceCollectionExtensions.cs WrtechedWhispers/WretchedWhispers.Tests/Persistence/EfUnitOfWorkTests.cs
git commit -m "feat: add IUnitOfWork transaction seam with EF implementation"
```

---

## Task 3: Rewrite `TurnCoordinator` to use both seams

Behavior-preserving refactor: swap the `DbContext` dependency for `IUnitOfWork`, move validation into the producer, replace the hand-rolled channel with `AsyncStreamBridge`. Update the test harness to inject a fake `IUnitOfWork` (no SQLite) and add a concurrency-conflict test.

**Files:**
- Modify: `WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs` (full rewrite)
- Modify: `WrtechedWhispers/WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs` (drop SQLite, inject fake UoW, add concurrency test)

- [ ] **Step 1: Rewrite `TurnCoordinator.cs`**

Replace the entire contents of `WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs` with:

```csharp
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Orchestrates one game-master turn: resolve the chat session, load context and derive the stage,
/// run the stage-scoped agent inside a single atomic unit of work, persist the exchange, and stream
/// the resulting events. Transaction mechanics live behind <see cref="IUnitOfWork"/> and the
/// channel/yield plumbing behind <see cref="AsyncStreamBridge"/> — this class is just the sequence.
/// </summary>
public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    IUnitOfWork unitOfWork,
    ILogger<TurnCoordinator> logger)
{
    public IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        CancellationToken ct) =>
        AsyncStreamBridge.Run<GameTurnEvent>(
            (writer, token) => ProduceEventsAsync(writer, sessionId, playerMessage, token),
            ct);

    private async Task ProduceEventsAsync(
        ChannelWriter<GameTurnEvent> writer,
        Guid sessionId,
        string playerMessage,
        CancellationToken ct)
    {
        using var activity = AgentToolProvider.ActivitySource.StartActivity("TurnCoordinator.ExecuteTurnAsync");
        activity?.SetTag("session.id", sessionId.ToString());

        // Resolve the chat session.
        var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
        var chatSessionId = chatSessions.FirstOrDefault();
        if (chatSessionId == Guid.Empty)
        {
            writer.TryWrite(new TurnError("No chat session found for this campaign"));
            return;
        }

        // Load context and derive the stage (locked for the whole turn).
        var context = await contextLoader.LoadAsync(sessionId, ct);
        if (context.Campaign is null)
        {
            writer.TryWrite(new TurnError("Session not found"));
            return;
        }

        var stage = context.DeriveStage();
        var (tools, registeredFunctions) = toolProvider.GetToolsForStage(context, stage);
        activity?.SetTag("session.stage", stage.ToString());
        activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

        try
        {
            // One atomic unit of work for the turn. Disposal rolls back if we don't commit.
            await using var uow = await unitOfWork.BeginAsync(ct);

            await chatHistoryRepository.SaveMessage(
                chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);

            var narrativeChunks = new List<NarrativeChunk>();
            var toolResults = new List<ToolResult>();

            // Every stage — including Combat — runs one agent turn per player message.
            await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
            {
                writer.TryWrite(evt);

                if (evt is NarrativeChunk chunk)
                    narrativeChunks.Add(chunk);
                else if (evt is ToolResult tool)
                    toolResults.Add(tool);
            }

            var fullResponse = new StringBuilder();
            foreach (var chunk in narrativeChunks)
                fullResponse.Append(chunk.Text);

            await chatHistoryRepository.SaveMessage(
                chatSessionId,
                new ChatMessage(ChatRole.Assistant, fullResponse.ToString()) { AuthorName = "Game_Master" },
                ct);

            await uow.CommitAsync(ct);

            // Reload post-commit so the client sees committed state.
            var postTurnContext = await contextLoader.LoadAsync(sessionId, ct);
            writer.TryWrite(StateUpdateMapper.Map(postTurnContext));
            writer.TryWrite(new TurnDone());

            logger.LogInformation(
                "Turn complete — Session={SessionId}, Stage={Stage}, NarrativeChunks={ChunkCount}, ToolResults={ToolCount}",
                sessionId, stage, narrativeChunks.Count, toolResults.Count);
        }
        catch (OperationCanceledException)
        {
            // uow disposal rolled back.
            writer.TryWrite(new TurnError("Request was cancelled"));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another turn for this same session committed while this one ran (double-submit / retry).
            // uow disposal rolled back. The SSE response is already open, so this is a turn-level error
            // rather than the pre-stream 409 the SessionConcurrencyGuard gives.
            logger.LogWarning(ex,
                "Concurrent turn conflict — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("This session was updated by another action. Please retry."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turn failed — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("An error occurred while processing your action"));
        }
    }
}
```

- [ ] **Step 2: Update the test harness in `TurnCoordinatorTests.cs`**

In `WrtechedWhispers/WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs`:

Replace the `using` block at the top (lines 1-8) with (drops the EF Core `DbContext` usings, keeps what the tests still need — `Moq`, models, services, the Infrastructure namespace for `IUnitOfWork`/`IChatHistoryRepository`, and adds `Microsoft.EntityFrameworkCore` for the `DbUpdateConcurrencyException` test):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;
```

Replace the class declaration, fields, constructor, `Dispose`, and `CreateCoordinator` (lines 12-47) with a version that injects a mocked `IUnitOfWork` and no longer needs SQLite or `IDisposable`:

```csharp
public class TurnCoordinatorTests
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _chatSessionId = Guid.NewGuid();

    private readonly Mock<ISessionContextLoader> _contextLoader = new();
    private readonly Mock<IAgentToolProvider> _toolProvider = new();
    private readonly Mock<IAgentExecutor> _agentExecutor = new();
    private readonly Mock<IChatHistoryRepository> _chatHistoryRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public TurnCoordinatorTests()
    {
        // A no-op unit of work: BeginAsync hands back a scope whose CommitAsync/DisposeAsync do nothing.
        var scope = new Mock<IUnitOfWorkScope>();
        scope.Setup(s => s.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scope.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _unitOfWork
            .Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope.Object);
    }

    private TurnCoordinator CreateCoordinator() =>
        new(
            _contextLoader.Object,
            _toolProvider.Object,
            _agentExecutor.Object,
            _chatHistoryRepo.Object,
            _unitOfWork.Object,
            NullLogger<TurnCoordinator>.Instance);
```

Leave the rest of the file (helpers `MakeExplorationContext`, `SetupChatSession`, `SetupToolsForExploration`, `SetupAgentExecutorStreaming`, `ToAsyncEnumerable`, the four existing `[Fact]`s, and `ThrowingAsyncEnumerable`) unchanged.

- [ ] **Step 3: Add the concurrency-conflict test**

In the same file, immediately after the existing `AgentExecutorThrows_ProducesTurnError` test (it ends at line 189 with its closing `}`), add:

```csharp
    [Fact]
    public async Task ConcurrencyConflict_ProducesRetryTurnError()
    {
        // Arrange — the agent run throws DbUpdateConcurrencyException (another turn committed first).
        SetupChatSession();

        var context = MakeExplorationContext();
        _contextLoader
            .Setup(l => l.LoadAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        SetupToolsForExploration();

        _agentExecutor
            .Setup(a => a.ExecuteAsync(
                It.IsAny<IReadOnlyList<AIFunction>>(),
                It.IsAny<SessionContext>(),
                _chatSessionId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(new DbUpdateConcurrencyException("conflict")));

        _chatHistoryRepo
            .Setup(r => r.SaveMessage(_chatSessionId, It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coordinator = CreateCoordinator();

        // Act
        var events = new List<GameTurnEvent>();
        await foreach (var evt in coordinator.ExecuteTurnAsync(_sessionId, "attack", CancellationToken.None))
            events.Add(evt);

        // Assert — the dedicated retry message, and no TurnDone.
        var error = events.OfType<TurnError>().First();
        Assert.Equal("This session was updated by another action. Please retry.", error.Message);
        Assert.DoesNotContain(events, e => e is TurnDone);
    }
```

- [ ] **Step 4: Run the full `TurnCoordinatorTests` suite**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj --filter "FullyQualifiedName~TurnCoordinatorTests"`
Expected: PASS — 5 passed (4 existing + 1 new). The existing `HappyPath`, `AgentExecutorThrows`, `NoCampaign`, `NoChatSession` still hold; behavior is preserved.

- [ ] **Step 5: Run the entire test suite (regression gate)**

Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj`
Expected: PASS — all green except the 2 pre-existing skipped `SessionStreamingTests`. Total should be 353 passed, 2 skipped (347 prior + AsyncStreamBridge 3 + EfUnitOfWork 2 + TurnCoordinator concurrency 1 = 353).

- [ ] **Step 6: Commit**

```bash
git add WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs WrtechedWhispers/WretchedWhispers.Tests/Services/TurnCoordinatorTests.cs
git commit -m "refactor: TurnCoordinator depends on IUnitOfWork and AsyncStreamBridge"
```

---

## Task 4: Final verification & PR

- [ ] **Step 1: Full solution build**

Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln`
Expected: `0 errors`.

- [ ] **Step 2: Confirm `TurnCoordinator` no longer references EF transactions or channels directly**

Run: `git grep -n "BeginTransactionAsync\|RollbackSafelyAsync\|Channel.CreateUnbounded\|WretchedWhispersDbContext" -- WrtechedWhispers/WretchedWhispers.Api/Services/TurnCoordinator.cs`
Expected: empty output (no matches). The only EF reference left in the file is the `DbUpdateConcurrencyException` catch, which is `git grep`-able separately and is intentional.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin refactor/turn-coordinator-seams
```
Then open the PR against `main` (compare URL printed by the push, or via the GitHub UI). Title: `Extract IUnitOfWork and AsyncStreamBridge seams from TurnCoordinator`. Hand over to the user to merge.

---

## Notes / Known follow-ups (out of scope)

- **Chat-history ownership smell** — `AgentExecutor` reads chat history while `TurnCoordinator` writes it. Deferred (see spec). Do not address here.
- The `DbUpdateConcurrencyException` in practice surfaces from a `SaveChangesAsync` inside the transaction (repository writes), not necessarily from `CommitAsync` — either way the coordinator's single `catch` handles it and `uow` disposal rolls back. No code change needed; just don't be surprised the UoW unit tests don't assert a throwing `CommitAsync`.
