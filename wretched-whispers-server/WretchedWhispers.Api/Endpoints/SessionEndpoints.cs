using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

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
        group.MapGet("/{sessionId:guid}/journal", GetSessionJournal);
        group.MapGet("/{sessionId:guid}/map", GetSessionMap);
        group.MapPost("/{sessionId:guid}/successor", CreateSuccessor);
        group.MapPost("/{sessionId:guid}/abandon", AbandonSession);

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
        IChatHistoryRepository chatHistoryRepo,
        CreateSessionRequest? request = null)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var campaign = Campaign.Create(
            request?.Difficulty ?? Difficulty.Grim,
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
            string? characterClass = null;
            int? currentHp = null;
            int? maxHp = null;

            Character? character = null;
            var firstPlayerId = campaign.Players.FirstOrDefault();
            if (firstPlayerId != Guid.Empty)
            {
                character = await charactersRepo.Get(firstPlayerId);
                if (character is not null)
                {
                    characterName = character.Name;
                    characterClass = character.Class == CharacterClass.Classless
                        ? null
                        : ClassPresets.For(character.Class).DisplayName;
                    currentHp = character.Hp.Current;
                    maxHp = character.Hp.Max;
                }
            }

            var status = DeriveStatus(campaign, character, firstPlayerId);

            var lastPlayed = await chatHistoryRepo.GetLastActivity(campaign.Id);

            previews.Add(new SessionPreviewDto(
                campaign.Id,
                campaign.Name,
                campaign.Description,
                characterName,
                currentHp,
                maxHp,
                status,
                campaign.Difficulty,
                lastPlayed,
                characterClass));
        }

        return Results.Ok(previews.OrderByDescending(p => p.LastPlayed ?? DateTime.MinValue));
    }

    private static async Task<IResult> GetSessionDetail(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        ISessionContextLoader contextLoader,
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
                        m.Role.Value,
                        m.Text,
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
            campaign.Difficulty,
            messages,
            totalMessages,
            page,
            pageSize,
            stateUpdate));
    }

    private static async Task<IResult> GetSessionMap(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        var pois = campaign.Pois
            .Select(p => new PoiDto(p.Name, p.Type.ToString(), p.X, p.Y, p.ConnectedTo))
            .ToList();

        return Results.Ok(new { pois, currentLocationName = campaign.CurrentLocationName });
    }

    private static async Task<IResult> GetSessionJournal(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        var entries = campaign.JournalEntries
            .Select(e => new JournalEntryDto(e.Category.ToString(), e.Text, e.Day, e.Hour))
            .ToList();

        var fallen = campaign.FallenCharacters
            .Select(f => new { name = f.Name, dayDied = f.DayDied })
            .ToList();

        return Results.Ok(new { entries, fallen });
    }

    private static async Task<IResult> CreateSuccessor(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo,
        ChatHistoryReducer chatHistoryReducer,
        SessionConcurrencyGuard guard)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        if (!await guard.TryAcquire(sessionId))
            return Results.Conflict(new { error = "GM response already in progress" });

        try
        {
            if (campaign.WorldEnded || campaign.IsEnded)
                return Results.Conflict(new { error = "This world has ended. Nothing walks it now." });

            var firstPlayerId = campaign.Players.FirstOrDefault();
            if (firstPlayerId == Guid.Empty)
                return Results.Conflict(new { error = "No character to bury." });

            var character = await charactersRepo.Get(firstPlayerId);
            if (character is null || !character.IsDead)
                return Results.Conflict(new { error = "The wretch still breathes." });

            var chronicles = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id);
            var fallenChronicleId = chronicles.FirstOrDefault();

            // Create the new chronicle first: if burial/save then fails, the campaign stays
            // unburied (retryable) and the orphan chronicle is just an empty, harmless row —
            // never a dead wretch stuck resuming inside their own chronicle. No compensation.
            var newChronicleId = await chatHistoryRepo.CreateSession(campaign.Id);

            campaign.BuryCharacter(character.Id, character.Name);
            await campaignsRepo.SaveCampaign(campaign, userId);

            if (fallenChronicleId != Guid.Empty)
                await chatHistoryReducer.SeedEpitaphAsync(fallenChronicleId, newChronicleId, http.RequestAborted);

            return Results.Ok(new { status = "character-creation" });
        }
        finally
        {
            guard.Release(sessionId);
        }
    }

    private static async Task<IResult> AbandonSession(
        Guid sessionId,
        HttpContext http,
        ICampaignsRepository campaignsRepo,
        SessionConcurrencyGuard guard)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        // Verify campaign exists and belongs to user
        var userCampaigns = await campaignsRepo.GetForUser(userId);
        var campaign = userCampaigns.FirstOrDefault(c => c.Id == sessionId);
        if (campaign is null)
            return Results.NotFound();

        if (!await guard.TryAcquire(sessionId))
            return Results.Conflict(new { error = "GM response already in progress" });

        try
        {
            if (!campaign.IsActive())
                return Results.Conflict(new { error = "This campaign has already ended." });

            campaign.End();
            await campaignsRepo.SaveCampaign(campaign, userId);
            return Results.Ok(new { status = "ended" });
        }
        finally
        {
            guard.Release(sessionId);
        }
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
                        m.Role.Value,
                        m.Text,
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

    // Same terminal truth as the live turn's state_update (StateUpdateMapper): status is a function of
    // the derived stage, which counts character death — not campaign flags alone.
    private static string DeriveStatus(Campaign campaign, Character? character, Guid firstPlayerId)
    {
        var context = new SessionContext { Campaign = campaign, Character = character };
        context.SetCampaignId(campaign.Id);
        if (firstPlayerId != Guid.Empty)
            context.SetCharacterId(firstPlayerId);
        return context.DeriveStatus();
    }
}
