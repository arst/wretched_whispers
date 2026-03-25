using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core;
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
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                var tenantContext = http.RequestServices.GetRequiredService<ITenantContext>();
                tenantContext.SetUserId(userId);
                return await next(context);
            });

        group.MapPost("/", CreateSession);
        group.MapGet("/", ListSessions);
        group.MapGet("/{sessionId:guid}", GetSessionDetail);
        group.MapGet("/{sessionId:guid}/messages", GetSessionMessages);

        group.MapPost("/{sessionId:guid}/actions", async (
            Guid sessionId,
            PlayerActionRequest request,
            TurnCoordinator turnCoordinator,
            SessionConcurrencyGuard guard,
            ICampaignsRepository campaignsRepo,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Verify ownership before acquiring concurrency lock
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            // Ownership check: returns 404 (not 403) to avoid information leakage, matching GetSessionDetail pattern
            var userCampaigns = await campaignsRepo.GetForUser(userId);
            if (!userCampaigns.Any(c => c.Id == sessionId))
                return Results.NotFound();

            // 409 check BEFORE any response writes
            if (!await guard.TryAcquire(sessionId))
                return Results.Conflict(new { error = "GM response already in progress" });

            return Results.ServerSentEvents(
                MapToSseItems(
                    WithGuardRelease(
                        turnCoordinator.ExecuteTurnAsync(sessionId, request.Message, ct),
                        guard,
                        sessionId)));
        });

        return app;
    }

    private static async IAsyncEnumerable<SseItem<string>> MapToSseItems(
        IAsyncEnumerable<GameTurnEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await foreach (var evt in events.WithCancellation(ct))
        {
            var json = JsonSerializer.Serialize<object>(evt, jsonOptions);
            yield return new SseItem<string>(json, evt.EventType);
        }
    }

    private static async IAsyncEnumerable<GameTurnEvent> WithGuardRelease(
        IAsyncEnumerable<GameTurnEvent> events,
        SessionConcurrencyGuard guard,
        Guid sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            await foreach (var evt in events.WithCancellation(ct))
                yield return evt;
        }
        finally
        {
            guard.Release(sessionId);
        }
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
        SessionContextLoader contextLoader,
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

        // Use SessionContextLoader + StateUpdateMapper to derive character/campaign state
        var context = await contextLoader.LoadAsync(sessionId);
        var stateUpdate = StateUpdateMapper.Map(context);

        return Results.Ok(new SessionDetailDto(
            sessionId,
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.CurrentDay,
            campaign.CurrentHour,
            stateUpdate.Status,
            messages,
            totalMessages,
            page,
            pageSize,
            stateUpdate.CharacterName,
            stateUpdate.CharacterHp,
            stateUpdate.CharacterMaxHp,
            stateUpdate.CharacterStrength,
            stateUpdate.CharacterAgility,
            stateUpdate.CharacterPresence,
            stateUpdate.CharacterToughness,
            stateUpdate.CharacterWeapon,
            stateUpdate.CharacterArmor,
            stateUpdate.CharacterInventory,
            stateUpdate.HasLostEye,
            stateUpdate.HasStabbedLung,
            stateUpdate.HasBrokenHand,
            stateUpdate.HasCrushedFoot,
            stateUpdate.HasSeveredArm,
            stateUpdate.HasSmashedFace,
            stateUpdate.IsInfected,
            stateUpdate.IsDizzyFromMagic,
            stateUpdate.IsEncumbered,
            stateUpdate.IsDead,
            stateUpdate.ArmorTier,
            stateUpdate.HasShield,
            stateUpdate.IsShieldBroken,
            stateUpdate.WorldEnded));
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
