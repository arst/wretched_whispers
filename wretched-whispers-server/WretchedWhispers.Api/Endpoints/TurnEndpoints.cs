using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Api.Endpoints;

public static class TurnEndpoints
{
    private const int MaxMessageLength = 4000;

    public static WebApplication MapTurnEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();
        group.MapPost("/sessions/{sessionId:guid}/turns", Submit);
        group.MapGet("/turns/{turnId:guid}", Status);
        group.MapGet("/turns/{turnId:guid}/events", Events);
        return app;
    }

    private static async Task<Results<Accepted<TurnResponse>, BadRequest<object>, NotFound>> Submit(Guid sessionId, SubmitTurnRequest request,
        HttpContext http, ICampaignsRepository campaigns, TurnQueue queue, CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return TypedResults.NotFound();
        http.RequestServices.GetRequiredService<IUserContext>().SetUserId(userId);
        if (http.User.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme
            && http.Request.Cookies.ContainsKey(".AspNetCore.Identity.Application")
            && !await http.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(http))
            return TypedResults.BadRequest(new { error = "Invalid antiforgery token." });
        var message = request.Message?.Trim();
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return TypedResults.BadRequest(new { error = "A request ID and a message of at most 4000 characters are required." });
        if (await campaigns.GetOwned(sessionId, ct) is null) return TypedResults.NotFound();
        try
        {
            var (turn, _) = await queue.EnqueueAsync(sessionId, userId, request.RequestId, message, ct);
            return TypedResults.Accepted($"/api/turns/{turn.Id}", ToResponse(turn));
        }
        catch (InvalidOperationException ex) { return TypedResults.BadRequest(new { error = ex.Message }); }
    }

    private static async Task<Results<Ok<TurnResponse>, NotFound>> Status(Guid turnId, HttpContext http, TurnQueue queue, CancellationToken ct)
    {
        var turn = await queue.GetOwnedAsync(turnId, http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        return turn is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(turn));
    }

    private static async Task Events(Guid turnId, HttpContext http, TurnQueue queue, TurnEventStore store, CancellationToken ct)
    {
        var turn = await queue.GetOwnedAsync(turnId, http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
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

    private static TurnResponse ToResponse(TurnRequestEntity turn) => new(turn.Id, turn.Status,
        $"/api/turns/{turn.Id}", $"/api/turns/{turn.Id}/events", turn.TerminalError);
}
