using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Orchestrates one game-master turn: resolve the chat session, load context and derive the stage,
/// run the stage-scoped agent inside a single atomic unit of work, persist the exchange, and stream
/// the resulting events. Player-visible outcome events (narrative, tool results) are emitted only
/// after the unit of work commits; a turn that fails emits only its <see cref="TurnError"/>.
/// Transaction mechanics live behind <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class TurnCoordinator(
    ISessionContextLoader contextLoader,
    IAgentToolProvider toolProvider,
    IAgentExecutor agentExecutor,
    IChatHistoryRepository chatHistoryRepository,
    ITurnTraceRepository turnTraceRepository,
    IUnitOfWork unitOfWork,
    ISessionLock sessionLock,
    TimeProvider clock,
    IServiceScopeFactory scopeFactory,
    ILogger<TurnCoordinator> logger)
{
    private static readonly JsonSerializerOptions TraceJson =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IAsyncEnumerable<GameTurnEvent> ExecuteTurnAsync(
        Guid sessionId,
        string playerMessage,
        Guid? turnId = null,
        CancellationToken ct = default,
        int? attempt = null) =>
        RunProducer(
            (writer, token) => ProduceEventsAsync(writer, sessionId, playerMessage, token, turnId, attempt),
            ct);

    private static async IAsyncEnumerable<GameTurnEvent> RunProducer(
        Func<ChannelWriter<GameTurnEvent>, CancellationToken, Task> produce,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<GameTurnEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = ProduceAndCompleteAsync(produce, channel.Writer, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
            yield return item;
    }

    private static async Task ProduceAndCompleteAsync(
        Func<ChannelWriter<GameTurnEvent>, CancellationToken, Task> produce,
        ChannelWriter<GameTurnEvent> writer,
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

    private async Task ProduceEventsAsync(
        ChannelWriter<GameTurnEvent> writer,
        Guid sessionId,
        string playerMessage,
        CancellationToken ct,
        Guid? turnId,
        int? attempt)
    {
        using var activity = AgentToolProvider.ActivitySource.StartActivity("TurnCoordinator.ExecuteTurnAsync");
        activity?.SetTag("session.id", sessionId.ToString());
        SessionStage? stage = null;

        // Filled in as the turn progresses, so every exit after the stage is known can leave a trace.
        // A trace for a turn that did NOT commit is written in the finally block, after the unit of work
        // has rolled back, on a fresh scope (see SaveDetachedTraceAsync).
        var started = clock.GetTimestamp();
        TraceDraft? draft = null;
        TurnTraceEntity? detachedTrace = null;
        var committed = false;

        try
        {
            // One atomic unit of work for the turn. Disposal rolls back if we don't commit.
            await using var uow = await unitOfWork.BeginAsync(ct);

            // One in-flight turn per session, across instances — acquired inside the transaction
            // (the Postgres lock rides it) and before any LLM call, so a busy session costs nothing.
            await using var lease = await sessionLock.TryAcquireAsync(sessionId, ct);
            if (lease is null)
            {
                writer.TryWrite(new TurnError("The narrator is already responding to another action. Please wait."));
                return; // uow disposal rolls back the empty transaction
            }

            // Read only after taking the lock. Otherwise another instance can commit between this
            // load and lock acquisition, leaving Character/Encounter aggregates stale and unversioned.
            var chatSessions = await chatHistoryRepository.GetSessionsForCampaign(sessionId, ct);
            var chatSessionId = chatSessions.FirstOrDefault();
            if (chatSessionId == Guid.Empty)
            {
                writer.TryWrite(new TurnError("No chat session found for this campaign"));
                return;
            }

            var context = await contextLoader.LoadAsync(sessionId, ct);
            if (context.Campaign is null)
            {
                writer.TryWrite(new TurnError("Session not found"));
                return;
            }

            var currentStage = context.DeriveStage();
            stage = currentStage;
            draft = new TraceDraft(sessionId, chatSessionId, currentStage, playerMessage, turnId, attempt,
                JsonSerializer.Serialize(StateUpdateMapper.Map(context), TraceJson));

            // Domain is final: a dead character or an ended world accepts no further actions. Do NOT run the
            // narrator — with no tools it cannot mutate anything, but it could still fabricate a "revival" in
            // prose (which is exactly what happened). Re-sync the client to the ended state and refuse the turn.
            if (currentStage == SessionStage.Ended)
            {
                logger.LogInformation("Turn refused — session already ended. Session={SessionId}", sessionId);
                var refusal = context.DeriveStatus() == "fallen"
                    ? "The wretch has fallen. Roll a new one or abandon this world."
                    : "This story has ended. Begin a new character to continue.";
                writer.TryWrite(StateUpdateMapper.Map(context));
                writer.TryWrite(new TurnError(refusal));
                draft.ToolsOffered = [];
                detachedTrace = BuildTrace(draft, TurnTraceOutcome.Refused, refusal, started);
                return;
            }

            var (tools, registeredFunctions) = toolProvider.GetToolsForStage(context, currentStage);
            draft.ToolsOffered = tools.Select(t => t.Name).ToList();
            activity?.SetTag("session.stage", currentStage.ToString());
            activity?.SetTag("session.functions", string.Join(", ", registeredFunctions));

            // Snapshot the state the model sees as INPUT for this turn, frozen before any tool mutates the
            // loaded aggregates (StateUpdate is an immutable record over copied values). Serves two roles:
            // the "why did the model do X" context for error analysis, and the BEFORE side of the per-turn
            // delta computed post-commit.
            var preTurnState = StateUpdateMapper.Map(context);

            var narrativeChunks = new List<NarrativeChunk>();
            var toolResults = draft.ToolResults;
            // The player-visible outcome, held back until the turn commits (see below), in the order
            // the executor produced it.
            var outcomeEvents = new List<GameTurnEvent>();
            AgentTrace? agentTrace = null;

            // Every stage — including Combat — runs one agent turn per player message.
            await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
            {
                // AgentTrace is an out-of-band capture event — persist it, never stream it to the client.
                if (evt is AgentTrace trace)
                {
                    agentTrace = draft.AgentTrace = trace;
                    continue;
                }

                outcomeEvents.Add(evt);

                if (evt is NarrativeChunk chunk)
                    narrativeChunks.Add(chunk);
                else if (evt is ToolResult tool)
                    toolResults.Add(tool);
            }

            var fullResponse = new StringBuilder();
            foreach (var chunk in narrativeChunks)
                fullResponse.Append(chunk.Text);
            draft.Narrative = fullResponse.ToString();

            // A run that produced neither prose nor tool effects is a failed turn (empty model
            // response, content filter), not a silent success: fail it and roll back so nothing is
            // persisted and the player can simply retry. Without this the turn "completed" with no
            // output and an empty assistant message polluted every later prompt.
            if (fullResponse.Length == 0 && toolResults.Count == 0)
            {
                logger.LogWarning(
                    "Empty agent response — no narrative and no tool calls. Session={SessionId}, Stage={Stage}",
                    sessionId, currentStage);
                writer.TryWrite(new TurnError("The narrator fell silent. Please try again."));
                detachedTrace = BuildTrace(draft, TurnTraceOutcome.Empty,
                    "Empty agent response: no narrative and no tool calls", started);
                return; // uow disposal rolls back
            }

            // Saved AFTER the agent run: the executor reloads history from this same DbContext and
            // appends the player message itself, so persisting it first would double it in the
            // conversation the model sees.
            await chatHistoryRepository.SaveMessage(
                chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct, turnId);
            // Tools may legitimately finish a turn with no prose (the delta still tells the story);
            // an EMPTY assistant message must never enter the history the model sees.
            if (fullResponse.Length > 0)
                await chatHistoryRepository.SaveMessage(
                    chatSessionId,
                    new ChatMessage(ChatRole.Assistant, fullResponse.ToString()) { AuthorName = "Game_Master" },
                    ct, turnId);

            await uow.CommitAsync(ct);
            committed = true;

            // Only now is the outcome real. Emitting it earlier let the TurnWorker persist narrative and
            // tool results that a failed save/commit (or a crash) then rolled back: the player read an
            // outcome that never happened, and a reclaimed retry replayed a second narrative beside it.
            // Error paths above emit only their TurnError, so a failed attempt leaves nothing behind.
            foreach (var evt in outcomeEvents)
                writer.TryWrite(evt);

            // Reload post-commit so the client sees committed state.
            var postTurnContext = await contextLoader.LoadAsync(sessionId, ct);
            var postTurnState = StateUpdateMapper.Map(postTurnContext);

            // Authoritative "what this turn changed", diffed from committed state — the source of truth
            // for the action's outcome, rendered beside the prose. Skipped when no character existed
            // before the turn (character creation is genesis, not a delta).
            var turnDelta = preTurnState.CharacterId is not null
                ? TurnDeltaMapper.Compute(preTurnState, postTurnState)
                : null;

            // Persist the committed turn's trace AFTER commit, best-effort: it needs the post-commit delta,
            // and a diagnostics write must never fail — or roll back — a turn the player already completed.
            try
            {
                await turnTraceRepository.Save(
                    BuildTrace(draft, TurnTraceOutcome.Committed, error: null, started, turnDelta), ct);
            }
            catch (Exception traceEx)
            {
                logger.LogError(traceEx,
                    "Turn trace persistence failed (turn already committed) — Session={SessionId}, Stage={Stage}",
                    sessionId, currentStage);
            }

            if (turnDelta is not null)
                writer.TryWrite(turnDelta);

            writer.TryWrite(postTurnState);
            writer.TryWrite(new TurnDone());

            logger.LogInformation(
                "Turn complete — Session={SessionId}, Stage={Stage}, NarrativeChunks={ChunkCount}, ToolResults={ToolCount}",
                sessionId, currentStage, narrativeChunks.Count, toolResults.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // uow disposal rolled back. No TurnError: the reader shares this token, so nothing
            // written here would ever be surfaced. An OCE thrown WITHOUT our token cancelled
            // (e.g. a dependency's internal timeout) falls through to the generic handler instead
            // of masquerading as user cancellation.
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another turn for this same session committed while this one ran (double-submit / retry).
            // uow disposal rolled back. The SSE response is already open, so this is a turn-level error
            // rather than the pre-stream 409 the SessionConcurrencyGuard gives.
            logger.LogWarning(ex,
                "Concurrent turn conflict — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("This session was updated by another action. Please retry."));
            if (draft is not null && !committed)
                detachedTrace = BuildTrace(draft, TurnTraceOutcome.Conflict, Describe(ex), started);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turn failed — Session={SessionId}, Stage={Stage}", sessionId, stage);
            writer.TryWrite(new TurnError("An error occurred while processing your action"));
            // After a commit the turn's own trace is (or failed to be) written above; a later failure
            // must not add a second row claiming the committed turn failed.
            if (draft is not null && !committed)
                detachedTrace = BuildTrace(draft, TurnTraceOutcome.Failed, Describe(ex), started);
        }
        finally
        {
            // The uow (declared inside the try) has been disposed — rolled back — by the time we get here.
            if (detachedTrace is not null)
                await SaveDetachedTraceAsync(detachedTrace);
        }
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Writes the trace of a turn that did not commit. Runs after the unit of work rolled back, on a
    /// fresh DI scope and DbContext: the turn's own context still tracks the rolled-back entities, and
    /// saving through it would re-commit them (the same hazard TurnQueue.CommitTerminalAsync clears its
    /// change tracker for). Best-effort: a trace failure never changes the turn's outcome.
    /// </summary>
    private async Task SaveDetachedTraceAsync(TurnTraceEntity trace)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITurnTraceRepository>()
                .Save(trace, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Turn trace persistence failed for a {Outcome} turn — Session={SessionId}",
                trace.Outcome, trace.CampaignId);
        }
    }

    /// <summary>What is known about the turn so far, for its trace.</summary>
    private sealed class TraceDraft(
        Guid campaignId, Guid chatSessionId, SessionStage stage, string playerMessage, Guid? turnId, int? attempt,
        string gameStateJson)
    {
        public Guid CampaignId { get; } = campaignId;
        public Guid ChatSessionId { get; } = chatSessionId;
        public SessionStage Stage { get; } = stage;
        public string PlayerMessage { get; } = playerMessage;
        public Guid? TurnId { get; } = turnId;
        public int? Attempt { get; } = attempt;
        public string GameStateJson { get; } = gameStateJson;
        public IReadOnlyList<string>? ToolsOffered { get; set; }
        public AgentTrace? AgentTrace { get; set; }
        public List<ToolResult> ToolResults { get; } = [];
        public string Narrative { get; set; } = string.Empty;
    }

    // Assembles one turn trace row. Tool-call arguments and tool results are stored as embedded JSON
    // (not escaped strings) so the exporter emits clean nested objects for error analysis.
    private TurnTraceEntity BuildTrace(
        TraceDraft draft, string outcome, string? error, long started, TurnDelta? turnDelta = null)
    {
        var agentTrace = draft.AgentTrace;
        var callsArray = new JsonArray();
        foreach (var call in agentTrace?.ToolCalls ?? [])
            callsArray.Add(new JsonObject
            {
                ["name"] = call.Name,
                ["arguments"] = call.Arguments is null ? null : JsonNode.Parse(call.Arguments)
            });

        var toolResultsJson = JsonSerializer.Serialize(
            draft.ToolResults.Select(t => new { name = t.Function, result = t.Result }), TraceJson);

        return new TurnTraceEntity
        {
            Id = Guid.NewGuid(),
            CampaignId = draft.CampaignId,
            ChatSessionId = draft.ChatSessionId,
            Stage = draft.Stage.ToString(),
            Timestamp = clock.GetUtcNow().UtcDateTime,
            PlayerMessage = draft.PlayerMessage,
            GameStateJson = draft.GameStateJson,
            ToolCallsJson = callsArray.ToJsonString(),
            ToolResultsJson = toolResultsJson,
            TurnDeltaJson = turnDelta is null ? null : JsonSerializer.Serialize(turnDelta, TraceJson),
            SuppressedNarrative = agentTrace?.SuppressedNarrative,
            Narrative = draft.Narrative,
            TurnId = draft.TurnId,
            Attempt = draft.Attempt,
            Outcome = outcome,
            Error = error,
            ModelId = agentTrace?.ModelId,
            PromptHash = agentTrace?.PromptHash,
            ToolsOffered = draft.ToolsOffered is null ? null : JsonSerializer.Serialize(draft.ToolsOffered),
            InputTokens = agentTrace?.InputTokens,
            OutputTokens = agentTrace?.OutputTokens,
            DurationMs = (long)clock.GetElapsedTime(started).TotalMilliseconds,
            Iterations = agentTrace?.Iterations
        };
    }
}
