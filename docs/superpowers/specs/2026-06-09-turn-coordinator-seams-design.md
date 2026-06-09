# TurnCoordinator: extract transaction & streaming seams

**Date:** 2026-06-09
**Status:** Approved (brainstorming), pending implementation plan
**Branch (planned):** `refactor/turn-coordinator-seams`

## Problem

`TurnCoordinator` is the orchestration spine — every player action (`POST /sessions/{id}/actions`)
becomes one `ExecuteTurnAsync` call. Orchestrating the turn sequence is legitimately its job, but
two **mechanical** concerns have leaked into the class and make it harder to read and test:

1. **EF transaction mechanics.** It depends on the concrete `WretchedWhispersDbContext` and calls
   `BeginTransactionAsync` / `CommitTransactionAsync` / `RollbackTransactionAsync` directly, plus a
   hand-rolled `RollbackSafelyAsync` swallow-helper. Infrastructure is welded into orchestration,
   and the coupling forces tests onto a real database.
2. **Streaming plumbing.** The unbounded `Channel` + `SingleWriter/SingleReader` + fire-and-forget
   producer + `ReadAllAsync` yield loop exists purely to work around C#'s "no `yield` inside
   `try/catch`" rule. It is generic, domain-free boilerplate sitting in the middle of the class.

Goal: extract both as seams so `TurnCoordinator` keeps the orchestration narrative (which reads
cleanly top-to-bottom and should stay that way) while no longer touching EF or channels.

### Explicit non-goal: do not over-decompose

The orchestration sequence must remain a single linear method. Shattering it into an `ITurnStep`
pipeline of micro-classes would reintroduce exactly the "hard to reason about" problem the recent
A–F refactor removed. A coordinator that reads as an ordered sequence is a feature, not a smell.

## Design

### 1. `IUnitOfWork` seam (transaction boundary)

Interface **and** implementation in `WretchedWhispers.Infrastructure.Persistence`, alongside the
existing `IChatHistoryRepository`. The dependency graph is Api → Infrastructure → Core, so a
consumer-owned interface in Api is impossible — `EfUnitOfWork` (in Infrastructure) could not
reference it. `TurnCoordinator` already imports `WretchedWhispers.Infrastructure.Persistence`, so it
depends on the interface there. This follows the precedent set when `IChatHistoryRepository` was moved
to Infrastructure.

```csharp
public interface IUnitOfWork
{
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken ct);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    // Lets DbUpdateConcurrencyException propagate to the caller.
    Task CommitAsync(CancellationToken ct);
    // DisposeAsync rolls back if CommitAsync was never called (absorbs RollbackSafelyAsync's
    // best-effort swallow semantics — a rollback failure on an already-closed connection is ignored).
}
```

**Implementation:** `EfUnitOfWork` takes the **scoped** `WretchedWhispersDbContext` and wraps the EF
transaction. Registered `AddScoped<IUnitOfWork, EfUnitOfWork>()` in `AddDomainServices`.

**Commit/rollback semantics (decided):** throw + auto-rollback on dispose. `CommitAsync` does not
catch `DbUpdateConcurrencyException` — it propagates so the coordinator can map it to a player-facing
`TurnError` (a UX policy decision that belongs in orchestration, not infrastructure). Disposal without
a prior commit rolls back, swallowing any rollback error (e.g. connection already closed).

**Critical correctness invariant (carried over, not changed):** the UoW must wrap the *same
request-scoped* `DbContext` that the agent's tools mutate through. That shared scope is what places
the tool writes inside the turn's transaction. The interface/impl docs must state this so the
registration lifetime is never changed to something that would break atomicity.

### 2. `AsyncStreamBridge` seam (streaming plumbing)

Generic, domain-agnostic helper in `WretchedWhispers.Api.Services`:

```csharp
public static class AsyncStreamBridge
{
    public static IAsyncEnumerable<T> Run<T>(
        Func<ChannelWriter<T>, CancellationToken, Task> produce,
        CancellationToken ct);
}
```

**Internals:** today's mechanics lifted verbatim — create the unbounded `SingleWriter/SingleReader`
channel, fire-and-forget `produce(writer, ct)`, `await foreach`-yield `reader.ReadAllAsync(ct)`. One
robustness improvement over the current inline code: the producer invocation is wrapped so
`writer.Complete()` always runs in a `finally`, and if `produce` throws (today it cannot — it catches
everything — but defensively) the channel is completed with that exception so the reader rethrows
instead of hanging.

**Deliberate boundary:** the bridge knows nothing about `GameTurnEvent` or `TurnError`. The
error→event mapping (the `try/catch` that writes `TurnError`) stays inside `ProduceEventsAsync`,
because it is domain/UX logic. The bridge only moves items.

### 3. Resulting `TurnCoordinator`

**Constructor deps:** drop `WretchedWhispersDbContext dbContext`; add `IUnitOfWork unitOfWork`. Keep
`contextLoader`, `toolProvider`, `agentExecutor`, `chatHistoryRepository`, `logger`.

**`ExecuteTurnAsync`** is no longer an iterator; it shrinks to roughly:

```csharp
return AsyncStreamBridge.Run<GameTurnEvent>(
    (writer, token) => ProduceEventsAsync(writer, sessionId, playerMessage, token),
    ct);
```

Because the method is no longer an iterator, the early-validation `TurnError`s (missing chat session;
null campaign) move *into* the produce delegate (write `TurnError` + `return`) so the bridge owns all
emission uniformly. Same observable behavior.

**`ProduceEventsAsync`** becomes the whole turn as a clean linear sequence:

1. resolve chat session → `TurnError` + return if missing
2. load context, derive stage, get tools → `TurnError` + return if campaign null
3. `await using var uow = await unitOfWork.BeginAsync(ct)`
4. save user message
5. run `agentExecutor.ExecuteAsync`, write events to the channel + accumulate narrative/tool results
6. save assistant message
7. `await uow.CommitAsync(ct)`
8. post-commit reload context → `StateUpdate`; then `TurnDone`
9. catch chain unchanged in behavior: `OperationCanceledException` / `DbUpdateConcurrencyException` /
   `Exception` → `TurnError`. Rollback now happens automatically via `uow` disposal, so the explicit
   `RollbackSafelyAsync` calls are deleted.

**Net deletions:** `RollbackSafelyAsync`, the manual `Begin/Commit/Rollback` trio, the channel
boilerplate. The `using Microsoft.EntityFrameworkCore;` stays only because the catch still names
`DbUpdateConcurrencyException`.

**Deliberately unchanged:** orchestration order; stage-locked-at-turn-start; tools-authoritative flow;
the post-commit reload (kept — the client must see committed truth); the two-layer concurrency defense
(`SessionConcurrencyGuard` 409 + the `Version` token).

## Testing

**New unit tests (enabled by the seams):**

- `AsyncStreamBridge` (pure, no DB): events written by the producer come out in order and the stream
  completes; a producer that throws surfaces via the reader without hanging and still completes;
  cancellation stops the reader.
- `EfUnitOfWork` (real SQLite via `SqliteTestBase`): commit persists changes; dispose-without-commit
  rolls back; `DbUpdateConcurrencyException` propagates out of `CommitAsync`.

**Existing tests:**

- `TurnCoordinatorTests` (real SQLite via `EnsureCreated`) keep running through the real
  `EfUnitOfWork` — unchanged behavior makes them the regression oracle. Only the constructor wiring
  changes (inject `IUnitOfWork` instead of the `DbContext`).
- The repository-level concurrency test (`SaveCampaign_…ThrowsConcurrencyException`) is untouched.

## Rollout

Single branch + PR (`refactor/turn-coordinator-seams`) — one cohesive structural change; splitting it
would leave an awkward half-state. Build + full suite green before handing over; user merges.

## Out of scope (YAGNI / deferred)

- **Chat-history ownership smell.** `AgentExecutor` *reads* chat history while `TurnCoordinator`
  *writes* it (split-brain). Real, but consolidating it is more invasive and the assistant-save is
  bound to the narrative assembly inside the transaction. Noted as a known follow-up; not in this PR.
- No change to the streaming protocol, stage logic, or `AgentExecutor`.
- No change to the console hosts.
