using System.Security.Claims;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Api.Endpoints;

public static class SessionEndpoints
{
    public static WebApplication MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/sessions")
            .RequireAuthorization();

        group.MapPost("/", CreateSession);
        group.MapGet("/", ListSessions);
        group.MapGet("/{sessionId:guid}", GetSessionDetail);
        group.MapGet("/{sessionId:guid}/messages", GetSessionMessages);

        group.MapPost("/{sessionId:guid}/actions", async (
            Guid sessionId,
            PlayerActionRequest request,
            GameSessionService gameService,
            SessionConcurrencyGuard guard,
            HttpContext http,
            CancellationToken ct) =>
        {
            // 409 check BEFORE any response writes (per research Pitfall 6)
            if (!await guard.TryAcquire(sessionId))
                return Results.Conflict(new { error = "GM response already in progress" });

            try
            {
                // Verify ownership before streaming
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                // Set SSE headers
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
            catch (OperationCanceledException)
            {
                // Client disconnected -- no error needed, just stop
                return Results.Empty;
            }
            catch (Exception)
            {
                // If response has started (headers sent), use SSE error event
                try
                {
                    await http.Response.WriteAsync("event: error\ndata: {\"message\":\"An unexpected error occurred\"}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
                catch
                {
                    // Response may be closed, swallow
                }

                return Results.Empty;
            }
            finally
            {
                guard.Release(sessionId);
            }
        });

        return app;
    }

    private static async Task<IResult> CreateSession(
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var campaign = Campaign.Create(
            DiceExpr.Parse("d6"),
            "New Campaign",
            "A new journey into doom");

        await campaignsRepo.SaveCampaign(campaign, userId);
        await chatHistoryRepo.CreateSession(campaign.Id);

        return Results.Created(
            $"/sessions/{campaign.Id}",
            new CreateSessionResponse(campaign.Id, campaign.Id));
    }

    private static async Task<IResult> ListSessions(
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var campaigns = await campaignsRepo.GetForUser(userId);
        var previews = new List<SessionPreviewDto>();

        foreach (var campaign in campaigns)
        {
            string? characterName = null;
            int? currentHp = null;
            int? maxHp = null;

            var firstPlayerId = campaign.Players.FirstOrDefault();
            if (firstPlayerId != Guid.Empty)
            {
                var character = await charactersRepo.Get(firstPlayerId);
                if (character is not null)
                {
                    characterName = character.Name;
                    currentHp = character.Hp.Current;
                    maxHp = character.Hp.Max;
                }
            }

            var status = DeriveStatus(campaign);

            // Check last played by looking at sessions for the campaign
            DateTime? lastPlayed = null;
            var sessions = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id);
            if (sessions.Count > 0)
            {
                // Use campaign creation as proxy since we don't track timestamps on chat sessions yet
                lastPlayed = DateTime.UtcNow;
            }

            previews.Add(new SessionPreviewDto(
                campaign.Id,
                campaign.Name,
                campaign.Description,
                characterName,
                currentHp,
                maxHp,
                status,
                lastPlayed));
        }

        return Results.Ok(previews);
    }

    private static async Task<IResult> GetSessionDetail(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        int page = 1,
        int pageSize = 50)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        var sessions = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id);
        var chatSessionId = sessions.FirstOrDefault();

        var messages = new List<ChatMessageDto>();
        var totalMessages = 0;

        if (chatSessionId != Guid.Empty)
        {
            var chatHistory = await chatHistoryRepo.LoadSession(chatSessionId);
            if (chatHistory is not null)
            {
                totalMessages = chatHistory.Count;
                var offset = (page - 1) * pageSize;
                messages = chatHistory
                    .Skip(offset)
                    .Take(pageSize)
                    .Select(m => new ChatMessageDto(
                        m.Role.Label,
                        m.Content,
                        m.AuthorName))
                    .ToList();
            }
        }

        var status = DeriveStatus(campaign);

        return Results.Ok(new SessionDetailDto(
            sessionId,
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.CurrentDay,
            campaign.CurrentHour,
            status,
            messages,
            totalMessages,
            page,
            pageSize));
    }

    private static async Task<IResult> GetSessionMessages(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        int page = 1,
        int pageSize = 50)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        var sessions = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id);
        var chatSessionId = sessions.FirstOrDefault();

        var messages = new List<ChatMessageDto>();
        var totalMessages = 0;

        if (chatSessionId != Guid.Empty)
        {
            var chatHistory = await chatHistoryRepo.LoadSession(chatSessionId);
            if (chatHistory is not null)
            {
                totalMessages = chatHistory.Count;
                var offset = (page - 1) * pageSize;
                messages = chatHistory
                    .Skip(offset)
                    .Take(pageSize)
                    .Select(m => new ChatMessageDto(
                        m.Role.Label,
                        m.Content,
                        m.AuthorName))
                    .ToList();
            }
        }

        return Results.Ok(new
        {
            messages,
            totalMessages,
            page,
            pageSize
        });
    }

    private static string DeriveStatus(Campaign campaign)
    {
        if (campaign.Players.Count == 0)
            return "character-creation";
        if (campaign.IsActive())
            return "in-progress";
        return "ended";
    }
}
