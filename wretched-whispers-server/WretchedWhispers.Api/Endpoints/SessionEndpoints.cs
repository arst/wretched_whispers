using WretchedWhispers.Api.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Endpoints;

public static class SessionEndpoints
{
    private const int MaxCharacterNameLength = 64;
    private const int MaxPageSize = 200;
    private static readonly TimeSpan RecapAfter = TimeSpan.FromHours(48);

    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sessions");

        // Mutating POSTs are transactional via WithUnitOfWork. GETs read only — a transaction would
        // add round-trips for nothing.
        group.MapPost("/", CreateSession).WithUnitOfWork();
        group.MapGet("/", ListSessions);
        group.MapGet("/{sessionId:guid}", GetSessionDetail);
        group.MapGet("/{sessionId:guid}/messages", GetSessionMessages);
        group.MapGet("/{sessionId:guid}/journal", GetSessionJournal);
        group.MapGet("/{sessionId:guid}/map", GetSessionMap);
        group.MapPost("/{sessionId:guid}/resume", ResumeSession).WithUnitOfWork();
        group.MapPost("/{sessionId:guid}/successor", CreateSuccessor).WithUnitOfWork();
        group.MapPost("/{sessionId:guid}/abandon", AbandonSession).WithUnitOfWork();

        return api;
    }

    /// <summary>
    /// Wraps the endpoint in one unit-of-work: begin before the handler, commit only on a 2xx
    /// result, roll back (via disposal) on everything else — early returns and exceptions alike.
    /// Also satisfies <see cref="ISessionLock"/>'s open-transaction requirement, so handlers can
    /// acquire the session lock without owning transaction plumbing.
    /// </summary>
    private static RouteHandlerBuilder WithUnitOfWork(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            await using var uow = await http.RequestServices.GetRequiredService<IUnitOfWork>()
                .BeginAsync(http.RequestAborted);

            var result = await next(context);

            if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
                await uow.CommitAsync(http.RequestAborted);
            return result;
        });

    private static async Task<IResult> CreateSession(
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        CharacterCreationService characterCreationService,
        CampaignService campaignService,
        CancellationToken ct,
        CreateSessionRequest? request = null)
    {
        if (!TryNormalizeCharacterName(request?.CharacterName, out var characterName, out var nameError))
            return Results.BadRequest(new { error = nameError });

        var difficulty = request?.Difficulty ?? Difficulty.Grim;

        // The player chose name and class; the domain rolls everything else. A null class means they asked
        // the dice to decide, and the die stays in the domain.
        var characterClass = request?.CharacterClass ?? characterCreationService.RollRandomClass();

        // WithUnitOfWork makes this atomic: a campaign without a character (or vice versa) is a
        // session the player cannot open, so all the writes commit or none do.
        var character = await characterCreationService.Create(characterName, difficulty, characterClass);
        var campaign = Campaign.Create(difficulty, "New Campaign", "A new journey into doom");

        await campaignsRepo.SaveCampaign(campaign);
        await campaignService.JoinCampaign(campaign.Id, character.Id);
        await chatHistoryRepo.CreateSession(campaign.Id, ct);

        return Results.Created(
            $"/api/sessions/{campaign.Id}",
            new CreateSessionResponse(campaign.Id, campaign.Id));
    }

    /// <summary>Validates the one free-text field the player controls. It reaches both the database and the
    /// narrator's prompt, so it is bounded here at the trust boundary rather than downstream.</summary>
    private static bool TryNormalizeCharacterName(string? raw, out string name, out string error)
    {
        name = raw?.Trim() ?? "";
        if (name.Length == 0)
        {
            error = "A wretch needs a name.";
            return false;
        }

        if (name.Length > MaxCharacterNameLength)
        {
            error = $"That name is too long; keep it under {MaxCharacterNameLength} characters.";
            return false;
        }

        error = "";
        return true;
    }

    private static async Task<IResult> ListSessions(
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo,
        CancellationToken ct)
    {
        var campaigns = await campaignsRepo.GetForUser(ct);
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

            var lastPlayed = await chatHistoryRepo.GetLastActivity(campaign.Id, ct);

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
        ICampaignsRepository campaignsRepo,
        ISessionContextLoader contextLoader,
        IChatHistoryRepository chatHistoryRepo,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        (page, pageSize) = ClampPaging(page, pageSize);
        var (messages, totalMessages, chatSessionId) =
            await LoadMessagePage(chatHistoryRepo, campaign.Id, page, pageSize, ct);

        // Use SessionContextLoader + StateUpdateMapper to derive character/campaign state
        var context = await contextLoader.LoadAsync(sessionId, ct);
        var stateUpdate = StateUpdateMapper.Map(context);
        var lastOpened = chatSessionId == Guid.Empty
            ? null
            : await chatHistoryRepo.GetLastOpened(chatSessionId, ct);
        var lastActivity = chatSessionId == Guid.Empty
            ? null
            : await chatHistoryRepo.GetSessionLastActivity(chatSessionId, ct);
        var recapDue = totalMessages > 0 && IsRecapDue(lastOpened, lastActivity, DateTime.UtcNow);

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
            stateUpdate,
            recapDue));
    }

    private static async Task<IResult> ResumeSession(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        ISessionContextLoader contextLoader,
        ChatHistoryReducer historyReducer,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        var chatSessionId = (await chatHistoryRepo.GetSessionsForCampaign(campaign.Id, ct))
            .FirstOrDefault();
        if (chatSessionId == Guid.Empty)
            return Results.Ok(new SessionResumeDto(null));

        var now = DateTime.UtcNow;
        var lastOpened = await chatHistoryRepo.GetLastOpened(chatSessionId, ct);
        var lastActivity = await chatHistoryRepo.GetSessionLastActivity(chatSessionId, ct);
        await chatHistoryRepo.MarkOpened(chatSessionId, now, ct);

        if (!IsRecapDue(lastOpened, lastActivity, now))
            return Results.Ok(new SessionResumeDto(null));

        var cached = await chatHistoryRepo.GetRecap(chatSessionId, ct);
        if (cached is not null && cached.ActivityAt == lastActivity)
            return Results.Ok(new SessionResumeDto(cached.Text));

        var context = await contextLoader.LoadAsync(campaign.Id, ct);
        var recap = await historyReducer.CreateRecapAsync(chatSessionId, context.FormatSnapshot(), ct);
        if (recap is not null && lastActivity is not null)
            await chatHistoryRepo.SaveRecap(chatSessionId, new ChatRecap(recap, lastActivity.Value), ct);
        return Results.Ok(new SessionResumeDto(recap));
    }

    private static bool IsRecapDue(DateTime? lastOpened, DateTime? lastActivity, DateTime now)
    {
        var lastSeen = new[] { lastOpened, lastActivity }.Max();
        return lastSeen is not null && now - lastSeen >= RecapAfter;
    }

    private static async Task<IResult> GetSessionMap(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        var pois = campaign.Pois
            .Select(p => new PoiDto(p.Name, p.Type.ToString(), p.X, p.Y, p.ConnectedTo))
            .ToList();

        return Results.Ok(new { pois, currentLocationName = campaign.CurrentLocationName });
    }

    private static async Task<IResult> GetSessionJournal(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
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
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo,
        ChatHistoryReducer chatHistoryReducer,
        CharacterCreationService characterCreationService,
        CampaignService campaignService,
        ISessionLock sessionLock,
        CancellationToken ct,
        CreateSuccessorRequest? request = null)
    {
        if (!TryNormalizeCharacterName(request?.CharacterName, out var successorName, out var nameError))
            return Results.BadRequest(new { error = nameError });

        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        await using var lease = await sessionLock.TryAcquireAsync(sessionId, ct);
        if (lease is null)
            return Results.Conflict(new { error = "GM response already in progress" });

        if (campaign.WorldEnded || campaign.IsEnded)
            return Results.Conflict(new { error = "This world has ended. Nothing walks it now." });

        var firstPlayerId = campaign.Players.FirstOrDefault();
        if (firstPlayerId == Guid.Empty)
            return Results.Conflict(new { error = "No character to bury." });

        var character = await charactersRepo.Get(firstPlayerId);
        if (character is null || !character.IsDead)
            return Results.Conflict(new { error = "The wretch still breathes." });

        var chronicles = await chatHistoryRepo.GetSessionsForCampaign(campaign.Id, ct);
        var fallenChronicleId = chronicles.FirstOrDefault();

        var newChronicleId = await chatHistoryRepo.CreateSession(campaign.Id, ct);

        campaign.BuryCharacter(character.Id, character.Name);
        await campaignsRepo.SaveCampaign(campaign);

        // The successor is rolled here rather than by the narrator, so the campaign is never left in a
        // characterless state. Difficulty is the campaign's own — a death does not renegotiate it.
        var successorClass = request?.CharacterClass ?? characterCreationService.RollRandomClass();
        var successor = await characterCreationService.Create(
            successorName, campaign.Difficulty, successorClass);
        await campaignService.JoinCampaign(campaign.Id, successor.Id);

        if (fallenChronicleId != Guid.Empty)
            await chatHistoryReducer.SeedEpitaphAsync(fallenChronicleId, newChronicleId, ct);

        return Results.Ok(new { status = "in-progress" });
    }

    private static async Task<IResult> AbandonSession(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        ISessionLock sessionLock,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        await using var lease = await sessionLock.TryAcquireAsync(sessionId, ct);
        if (lease is null)
            return Results.Conflict(new { error = "GM response already in progress" });

        if (!campaign.IsActive())
            return Results.Conflict(new { error = "This campaign has already ended." });

        campaign.End();
        await campaignsRepo.SaveCampaign(campaign);
        return Results.Ok(new { status = "ended" });
    }

    private static async Task<IResult> GetSessionMessages(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return Results.NotFound();

        (page, pageSize) = ClampPaging(page, pageSize);
        var (messages, totalMessages, _) =
            await LoadMessagePage(chatHistoryRepo, campaign.Id, page, pageSize, ct);

        return Results.Ok(new
        {
            messages,
            totalMessages,
            page,
            pageSize
        });
    }

    private static (int Page, int PageSize) ClampPaging(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>Pages the campaign's first chronicle.</summary>
    // ponytail: LoadSession pulls the whole history and pages in memory; push paging into the
    // repository query if chronicles outgrow this.
    private static async Task<(List<ChatMessageDto> Messages, int Total, Guid ChatSessionId)> LoadMessagePage(
        IChatHistoryRepository chatHistoryRepo, Guid campaignId, int page, int pageSize, CancellationToken ct)
    {
        var chatSessionId = (await chatHistoryRepo.GetSessionsForCampaign(campaignId, ct)).FirstOrDefault();
        if (chatSessionId == Guid.Empty)
            return ([], 0, chatSessionId);

        var chatHistory = await chatHistoryRepo.LoadSession(chatSessionId, ct);
        if (chatHistory is null)
            return ([], 0, chatSessionId);

        var messages = chatHistory
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ChatMessageDto(m.Role.Value, m.Text, m.AuthorName))
            .ToList();

        return (messages, chatHistory.Count, chatSessionId);
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
