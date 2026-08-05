using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Endpoints;

public static class TurnEndpoints
{
    private const int MaxMessageLength = 4000;

    public static RouteGroupBuilder MapTurnEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/sessions/{sessionId:guid}/turns", Submit);
        api.MapGet("/turns/{turnId:guid}/events", Events);
        return api;
    }

    private static async Task<IResult> Submit(Guid sessionId, SubmitTurnRequest request,
        IUserContext user, ICampaignsRepository campaigns, TurnQueue queue, CancellationToken ct)
    {
        var message = request.Message?.Trim();
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return TypedResults.BadRequest(new { error = "A request ID and a message of at most 4000 characters are required." });
        if (await campaigns.GetOwned(sessionId, ct) is null) return TypedResults.NotFound();
        try
        {
            var (turn, _) = await queue.EnqueueAsync(sessionId, user.UserId, request.RequestId, message, ct);
            return TypedResults.Accepted($"/api/turns/{turn.Id}", new TurnResponse(turn.Id, turn.TerminalError));
        }
        catch (InvalidOperationException ex) { return TypedResults.BadRequest(new { error = ex.Message }); }
    }

    private static async Task Events(Guid turnId, HttpContext http, IUserContext user,
        TurnQueue queue, TurnEventStore store, CancellationToken ct)
    {
        var turn = await queue.GetOwnedAsync(turnId, user.UserId, ct);
        if (turn is null) { http.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var sequence = long.TryParse(http.Request.Headers["Last-Event-ID"], out var last) ? last : 0;
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        while (!ct.IsCancellationRequested)
        {
            var items = await store.ReadAfterAsync(turnId, sequence, ct);
            foreach (var item in items)
            {
                sequence = item.Sequence;
                await http.Response.WriteAsync($"id: {item.Sequence}\nevent: {item.EventType}\ndata: {item.Payload}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
                if (item.EventType is "done" or "error") return;
            }
            await http.Response.WriteAsync(": keepalive\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
            await Task.Delay(1000, ct);
        }
    }
}
