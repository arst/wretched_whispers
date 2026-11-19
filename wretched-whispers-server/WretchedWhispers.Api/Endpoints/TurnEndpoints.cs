using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Api.Endpoints;

public static class TurnEndpoints
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static RouteGroupBuilder MapTurnEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/sessions/{sessionId:guid}/turns", Submit)
            .RequireRateLimiting(RateLimitPolicies.Turns);
        api.MapGet("/turns/{turnId:guid}/events", Events);
        api.MapPost("/turns/{turnId:guid}/cancel", Cancel);
        return api;
    }

    /// <summary>
    /// Asks to cancel the caller's own turn. Idempotent. A running turn is not cancelled by this call
    /// itself: its worker stops the run within about a second and ends the event stream, which stays
    /// the single place the client learns the outcome.
    /// </summary>
    private static async Task<Results<Ok<TurnCancelResponse>, NotFound>> Cancel(
        Guid turnId, IUserContext user, TurnQueue queue, CancellationToken ct) =>
        await queue.RequestCancelAsync(turnId, user.UserId, ct) switch
        {
            TurnCancelResult.Cancelled => TypedResults.Ok(new TurnCancelResponse("cancelled")),
            TurnCancelResult.Requested => TypedResults.Ok(new TurnCancelResponse("cancelling")),
            TurnCancelResult.AlreadyFinished => TypedResults.Ok(new TurnCancelResponse("finished")),
            _ => TypedResults.NotFound()
        };

    private static async Task<Results<Accepted<TurnResponse>, NotFound, ProblemHttpResult>> Submit(
        Guid sessionId, SubmitTurnRequest request,
        IUserContext user, ICampaignsRepository campaigns, TurnQueue queue, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty)
            return ApiProblem.BadRequest("A request ID is required.");
        if (!PlayerInput.TryTurnMessage(request.Message, out var message, out var messageError))
            return ApiProblem.BadRequest(messageError);

        if (await campaigns.GetOwned(sessionId, ct) is null)
            return TypedResults.NotFound();

        var enqueued = await queue.EnqueueAsync(sessionId, user.UserId, request.RequestId, message, ct);
        if (enqueued.Turn is not { } turn)
            return ApiProblem.BadRequest("That request ID was already used for a different action.");

        return TypedResults.Accepted($"/api/turns/{turn.Id}", new TurnResponse(turn.Id, turn.TerminalError));
    }

    /// <summary>
    /// Replays the turn's events from <c>Last-Event-ID</c>, then tails it. Framing, flushing and the
    /// content type are the SSE result's job; this only decides what to yield.
    /// </summary>
    private static async Task<Results<ServerSentEventsResult<string>, NotFound>> Events(
        Guid turnId, HttpContext http, IUserContext user,
        TurnQueue queue, TurnEventStore store, CancellationToken ct)
    {
        var turn = await queue.GetOwnedAsync(turnId, user.UserId, ct);
        if (turn is null)
            return TypedResults.NotFound();

        var from = long.TryParse(http.Request.Headers["Last-Event-ID"], out var last) ? last : 0;
        var alreadyFinished = turn.Status is TurnStatus.Completed or TurnStatus.Failed;

        return TypedResults.ServerSentEvents(
            Stream(store, turnId, from, alreadyFinished, turn.TerminalError, ct));
    }

    private static async IAsyncEnumerable<SseItem<string>> Stream(
        TurnEventStore store, Guid turnId, long from, bool alreadyFinished, string? terminalError,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sequence = from;

        while (!ct.IsCancellationRequested)
        {
            List<TurnEventEntity> items;
            try
            {
                items = await store.ReadAfterAsync(turnId, sequence, ct);
            }
            catch (OperationCanceledException)
            {
                // The client hung up. Not an error, and not worth a logged stack trace per disconnect.
                yield break;
            }

            foreach (var item in items)
            {
                sequence = item.Sequence;
                // The payload is already serialised JSON, so it rides as the raw data line.
                yield return new SseItem<string>(item.Payload, item.EventType)
                {
                    EventId = item.Sequence.ToString()
                };
                if (item.EventType is "done" or "error")
                    yield break;
            }

            // A turn that finished before this connection opened has no further events coming. Without
            // this, reconnecting past the terminal event tails a completed turn forever, sending
            // keepalives to a client waiting for an ending it can no longer receive.
            if (alreadyFinished)
            {
                yield return terminalError is null
                    ? new SseItem<string>("{}", "done")
                    : new SseItem<string>(JsonSerializer.Serialize(new { message = terminalError }), "error");
                yield break;
            }

            yield return new SseItem<string>("", "keepalive");

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
