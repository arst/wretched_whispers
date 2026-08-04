# Durable Turns and Resumable SSE — Implementation Plan

**Goal:** Make a game turn survive browser disconnects, ingress timeouts, replica restarts and
blue-green traffic changes by separating durable execution from event delivery.

**Depends on:** deployment profiles for final Server verification. The domain/API work can begin in
parallel with Azure infrastructure after the profile contract is stable.

## Target protocol

```text
POST /api/sessions/{sessionId}/turns  ──► durable queued turn ──► background worker
          │                                                   │
          └── 202 { turnId, eventsUrl }                        ├── TurnCoordinator
                                                              └── sequenced events

GET /api/turns/{turnId}/events  ◄──────── replay + live SSE from persisted events
GET /api/turns/{turnId}         ◄──────── status/result fallback
```

## Constraints

- Keep SSE; do not add raw WebSockets, SignalR, Redis or a cloud queue in this phase.
- A client disconnect must never cancel an accepted turn.
- Submission is idempotent; retrying a timed-out POST must not execute twice.
- PostgreSQL coordinates workers across replicas; SQLite remains correct for one-process profiles.
- The existing session lock remains the authority for one active turn per campaign.
- Preserve `TurnCoordinator`'s atomic domain transaction and existing event types.
- Event delivery is replayable by monotonically increasing sequence number.
- Old `/actions` clients remain compatible for one rollout, then the endpoint is removed separately.

## Task 1: Add durable turn and event models

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Infrastructure/Persistence/Entities/TurnRequestEntity.cs`
- Create: `wretched-whispers-server/WretchedWhispers.Infrastructure/Persistence/Entities/TurnEventEntity.cs`
- Modify: `wretched-whispers-server/WretchedWhispers.Infrastructure/Persistence/WretchedWhispersDbContext.cs`
- Add SQLite and PostgreSQL migrations
- Test: create `wretched-whispers-server/WretchedWhispers.Tests/Persistence/TurnQueueTests.cs`

- [ ] `TurnRequest`: ID, campaign/session ID, user ID, client request ID, player message, status,
      attempt count, lease owner/expiry, timestamps and terminal error.
- [ ] `TurnEvent`: turn ID, sequence, event type, serialized payload and timestamp.
- [ ] Unique constraints on `(UserId, ClientRequestId)` and `(TurnId, Sequence)`.
- [ ] Index pending turns by status/creation time and events by turn/sequence.
- [ ] Define statuses `Pending`, `Running`, `Completed`, and `Failed`; do not add cancellation until a
      user-facing cancel feature exists.
- [ ] Verify both provider migration sets produce equivalent constraints.

## Task 2: Implement idempotent submission and status

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Api/Endpoints/TurnEndpoints.cs`
- Create: request/response models under `WretchedWhispers.Api/Models`
- Test: create `wretched-whispers-server/WretchedWhispers.Tests/Turns/TurnEndpointTests.cs`

- [ ] `POST /api/sessions/{sessionId}/turns` requires an authenticated owner and a client-generated UUID
      request ID.
- [ ] Insert the pending turn and return `202 Accepted` with `turnId`, `statusUrl`, and `eventsUrl`.
- [ ] Repeating the same request ID returns the original turn without another queue row.
- [ ] Reject a different payload reusing the same request ID.
- [ ] `GET /api/turns/{turnId}` returns owner-scoped status and terminal error, never another user's turn.
- [ ] Bound and validate the player message at this trust boundary.

## Task 3: Claim work safely across replicas

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Infrastructure/Persistence/TurnQueue.cs`
- Create: `wretched-whispers-server/WretchedWhispers.Engine/Services/TurnWorker.cs`
- Modify: service registration
- Test: create concurrency tests under `WretchedWhispers.Tests/Turns`

- [ ] Claim one pending row with an atomic conditional status update; only the winner runs it.
- [ ] Use a lease expiry so a turn abandoned by a dead replica becomes claimable again.
- [ ] Keep attempts bounded. After the limit, mark failed and append one terminal error event.
- [ ] Run `TurnCoordinator.ExecuteTurnAsync` under the worker's lifetime token, not an HTTP request
      token.
- [ ] Continue using the existing cross-instance session lock before any LLM call.
- [ ] Prove with concurrent workers that one turn executes exactly once at a time.

The initial worker may poll PostgreSQL with a short bounded delay. Add a queue service only when
measured load makes polling inadequate.

## Task 4: Persist coordinator events independently

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Infrastructure/Persistence/TurnEventStore.cs`
- Modify: `TurnWorker.cs`
- Test: create `TurnEventStoreTests.cs`

- [ ] Enumerate the existing `TurnCoordinator` event stream and persist each public event with the
      next sequence number.
- [ ] Use a separate short-lived DbContext/transaction for event writes so narrative progress is
      visible while the domain transaction remains open.
- [ ] Never persist the internal `AgentTrace` event.
- [ ] Append exactly one terminal `done` or `error` event and then set the matching turn status.
- [ ] If the worker dies after a domain commit but before writing `done`, reconcile from persisted
      chat/trace state before retrying; never rerun an already committed turn.
- [ ] Add a crash-boundary test around claim, domain commit, and terminal event persistence.

The last item is the critical correctness gate. A lease alone prevents simultaneous execution but
does not prevent duplicate execution after a crash; the turn ID must be recorded with the committed
chat exchange or trace so completion can be recognized idempotently.

## Task 5: Add replayable SSE delivery

**Files:**

- Modify: `TurnEndpoints.cs`
- Test: create `TurnStreamingTests.cs`

- [ ] `GET /api/turns/{turnId}/events` validates ownership before opening the stream.
- [ ] Read the starting sequence from standard `Last-Event-ID`, falling back to zero.
- [ ] Replay persisted events in order, then wait/poll for new events until a terminal event appears.
- [ ] Set the SSE `id` field to the event sequence and preserve existing event names/payloads.
- [ ] Send lightweight comment heartbeats frequently enough to avoid idle proxy termination.
- [ ] Ending an HTTP stream never changes turn status or cancels execution.
- [ ] Test initial replay, reconnect after a sequence, no duplicates, authorization, terminal close,
      and disconnect while the worker continues.

## Task 6: Switch the React client to submit then subscribe

**Files:**

- Replace/rename: `wretched-whispers-web/src/hooks/useSseStream.ts`
- Modify: `wretched-whispers-web/src/lib/api.ts`
- Modify: `wretched-whispers-web/src/stores/sessionStore.ts`
- Add focused frontend tests

- [ ] Generate one client request UUID per player action and retain it across submission retries.
- [ ] POST the command, store `turnId`, then open the returned event URL.
- [ ] Track the last applied sequence and reconnect with `Last-Event-ID` after EOF/network loss.
- [ ] On page reload, query the session's active turn and resume it before allowing another action.
- [ ] Keep existing narrative/tool/state handlers; only the transport lifecycle changes.
- [ ] Poll the status endpoint as a fallback after repeated stream failures.
- [ ] Remove the current behavior that retries by submitting the player action again.

## Task 7: Roll out compatibly

- [ ] Deploy the new schema and endpoints while retaining `/api/sessions/{id}/actions`.
- [ ] Deploy the new bundled frontend and observe completion, reconnect, duplicate and latency metrics.
- [ ] Exercise a blue-green traffic change during an active turn and verify reconnection to another
      revision.
- [ ] Remove `/actions` and its request-bound Channel path only in a later release.
- [ ] Add retention cleanup for old per-chunk events after a documented period; preserve the compact
      final chat history and turn trace.

## Acceptance gate

- [ ] Closing or refreshing the browser during a turn does not cancel or duplicate it.
- [ ] Killing the serving API replica does not lose accepted work or already persisted events.
- [ ] A client reconnects through another replica and receives each event once in order.
- [ ] Repeating command submission with the same request ID returns the same turn.
- [ ] Two replicas cannot execute the same turn concurrently.
- [ ] A crash after domain commit cannot cause the turn's tools to run twice.
- [ ] A turn may exceed one HTTP connection's lifetime because execution is connection-independent.
- [ ] Existing domain, endpoint and frontend tests remain green.

## Deliberately deferred

- WebSockets/SignalR; add only for genuinely bidirectional real-time features.
- Azure SignalR Service, Redis backplane, Service Bus and distributed notifications.
- User cancellation and priority queues.
- Permanent storage of every narrative chunk; cleanup may compact completed events.
