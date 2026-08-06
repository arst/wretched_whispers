using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Api.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Api.Endpoints;

public static class SessionEndpoints
{
    private const int MaxPageSize = 200;
    private static readonly TimeSpan RecapAfter = TimeSpan.FromHours(48);

    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sessions");

        // Mutating POSTs that touch more than one aggregate are transactional via WithUnitOfWork.
        // GETs read only, and /resume writes two independent rows around a model call — a
        // transaction there would hold the database open for the length of an LLM round trip.
        group.MapPost("/", CreateSession).WithUnitOfWork();
        group.MapGet("/", ListSessions);
        group.MapGet("/{sessionId:guid}", GetSessionDetail);
        group.MapGet("/{sessionId:guid}/messages", GetSessionMessages);
        group.MapGet("/{sessionId:guid}/journal", GetSessionJournal);
        group.MapGet("/{sessionId:guid}/map", GetSessionMap);
        group.MapPost("/{sessionId:guid}/resume", ResumeSession);
        group.MapPost("/{sessionId:guid}/successor", CreateSuccessor).WithUnitOfWork();
        group.MapPost("/{sessionId:guid}/abandon", AbandonSession).WithUnitOfWork();

        return group;
    }

    /// <summary>
    /// Wraps the endpoint in one unit-of-work: begin before the handler, commit on any non-failure
    /// result, roll back (via disposal) on a failure status or an exception.
    /// Also satisfies <see cref="ISessionLock"/>'s open-transaction requirement, so handlers can
    /// acquire the session lock without owning transaction plumbing.
    /// </summary>
    private static RouteHandlerBuilder WithUnitOfWork(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            await using var uow = await http.RequestServices.GetRequiredService<IUnitOfWork>()
                .BeginAsync(http.RequestAborted);

            try
            {
                var result = await next(context);

                // Commit unless the handler explicitly failed. Handlers return Results<...> unions,
                // which nest the actual result — unwrap before reading the status, else every union
                // reads as 200 and a failure result would commit its writes. Testing for success
                // instead would treat any result that carries no status code
                // (IStatusCodeHttpResult.StatusCode is int?) as a failure and silently discard its
                // writes while still answering 200.
                var unwrapped = result;
                while (unwrapped is INestedHttpResult nested)
                    unwrapped = nested.Result;

                var status = (unwrapped as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
                if (status >= StatusCodes.Status400BadRequest)
                    return result;

                await uow.CommitAsync(http.RequestAborted);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The campaign's Version token lost a race with a turn committing concurrently. The
                // token is checked at SaveChangesAsync inside the handler, not at commit, so the
                // catch must cover the handler too. IUnitOfWorkScope deliberately leaves this to the
                // caller; this is that caller.
                return ApiProblem.Conflict("The session changed while this action was in flight. Try again.");
            }
        });

    private static async Task<Results<Created<CreateSessionResponse>, ProblemHttpResult>> CreateSession(
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        CharacterCreationService characterCreationService,
        CampaignService campaignService,
        CancellationToken ct,
        CreateSessionRequest? request = null)
    {
        if (!PlayerInput.TryCharacterName(request?.CharacterName, out var characterName, out var nameError))
            return ApiProblem.BadRequest(nameError);

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

        return TypedResults.Created(
            $"/api/sessions/{campaign.Id}",
            new CreateSessionResponse(campaign.Id));
    }

    private static async Task<Ok<IReadOnlyList<SessionPreviewDto>>> ListSessions(
        ICampaignsRepository campaignsRepo,
        ICharactersRepository charactersRepo,
        IChatHistoryRepository chatHistoryRepo,
        CancellationToken ct)
    {
        var campaigns = await campaignsRepo.GetForUser(ct);
        if (campaigns.Count == 0)
            return TypedResults.Ok<IReadOnlyList<SessionPreviewDto>>([]);

        // Three queries total, not three per campaign: the character rows and the activity
        // timestamps are each fetched for the whole page in one go.
        var campaignIds = campaigns.Select(c => c.Id).ToList();
        var playerIds = campaigns
            .Select(c => c.Players.FirstOrDefault())
            .Where(id => id != Guid.Empty)
            .ToList();

        var characters = await charactersRepo.GetMany(playerIds, ct);
        var lastActivity = await chatHistoryRepo.GetLastActivityForCampaigns(campaignIds, ct);

        var previews = new List<SessionPreviewDto>(campaigns.Count);
        foreach (var campaign in campaigns)
        {
            var playerId = campaign.Players.FirstOrDefault();
            var character = playerId != Guid.Empty && characters.TryGetValue(playerId, out var found)
                ? found
                : null;

            previews.Add(new SessionPreviewDto(
                campaign.Id,
                campaign.Name,
                campaign.Description,
                character?.Name,
                character?.Hp.Current,
                character?.Hp.Max,
                SessionContext.For(campaign, playerId, character).DeriveStatus(),
                campaign.Difficulty,
                lastActivity.TryGetValue(campaign.Id, out var played) ? played : null,
                DisplayClass(character)));
        }

        previews.Sort((a, b) => (b.LastPlayed ?? DateTime.MinValue).CompareTo(a.LastPlayed ?? DateTime.MinValue));
        return TypedResults.Ok<IReadOnlyList<SessionPreviewDto>>(previews);
    }

    /// <summary>Classless is the absence of a class on the wire — the card shows no class line rather
    /// than the words "Classless Scum".</summary>
    private static string? DisplayClass(Character? character) =>
        character is null || character.Class == CharacterClass.Classless
            ? null
            : ClassPresets.For(character.Class).DisplayName;

    private static async Task<Results<Ok<SessionDetailDto>, NotFound>> GetSessionDetail(
        Guid sessionId,
        ISessionContextLoader contextLoader,
        IChatHistoryRepository chatHistoryRepo,
        TimeProvider clock,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        // One load covers both the ownership check and the state the view needs.
        var context = await contextLoader.LoadOwnedAsync(sessionId, ct);
        if (context?.Campaign is not { } campaign)
            return TypedResults.NotFound();

        (page, pageSize) = ClampPaging(page, pageSize);
        var chronicleId = await chatHistoryRepo.GetActiveChronicle(campaign.Id, ct);
        var (messages, totalMessages) = await LoadMessagePage(chatHistoryRepo, chronicleId, page, pageSize, ct);

        var stateUpdate = StateUpdateMapper.Map(context);
        var lastOpened = chronicleId is null ? null : await chatHistoryRepo.GetLastOpened(chronicleId.Value, ct);
        var lastActivity = chronicleId is null
            ? null
            : await chatHistoryRepo.GetSessionLastActivity(chronicleId.Value, ct);
        var recapDue = totalMessages > 0 && IsRecapDue(lastOpened, lastActivity, clock.GetUtcNow().UtcDateTime);

        return TypedResults.Ok(new SessionDetailDto(
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

    /// <summary>
    /// Deliberately not transactional: <see cref="ChatHistoryReducer.CreateRecapAsync"/> calls the
    /// model, and holding a write transaction open across that round trip blocks every other writer
    /// for seconds. The two writes here are independent single rows — marking the chronicle opened
    /// stands on its own even if the recap fails, and the recap is a cache.
    /// </summary>
    private static async Task<Results<Ok<SessionResumeDto>, NotFound>> ResumeSession(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        ISessionContextLoader contextLoader,
        ChatHistoryReducer historyReducer,
        TimeProvider clock,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        var chronicleId = await chatHistoryRepo.GetActiveChronicle(campaign.Id, ct);
        if (chronicleId is null)
            return TypedResults.Ok(new SessionResumeDto(null));

        var now = clock.GetUtcNow().UtcDateTime;
        var lastOpened = await chatHistoryRepo.GetLastOpened(chronicleId.Value, ct);
        var lastActivity = await chatHistoryRepo.GetSessionLastActivity(chronicleId.Value, ct);
        await chatHistoryRepo.MarkOpened(chronicleId.Value, now, ct);

        if (!IsRecapDue(lastOpened, lastActivity, now))
            return TypedResults.Ok(new SessionResumeDto(null));

        var cached = await chatHistoryRepo.GetRecap(chronicleId.Value, ct);
        if (cached is not null && cached.ActivityAt == lastActivity)
            return TypedResults.Ok(new SessionResumeDto(cached.Text));

        var context = await contextLoader.LoadAsync(campaign.Id, ct);
        var recap = await historyReducer.CreateRecapAsync(chronicleId.Value, context.FormatSnapshot(), ct);
        if (recap is not null && lastActivity is not null)
            await chatHistoryRepo.SaveRecap(chronicleId.Value, new ChatRecap(recap, lastActivity.Value), ct);
        return TypedResults.Ok(new SessionResumeDto(recap));
    }

    private static bool IsRecapDue(DateTime? lastOpened, DateTime? lastActivity, DateTime now)
    {
        var lastSeen = new[] { lastOpened, lastActivity }.Max();
        return lastSeen is not null && now - lastSeen >= RecapAfter;
    }

    private static async Task<Results<Ok<SessionMapDto>, NotFound>> GetSessionMap(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        var pois = campaign.Pois
            .Select(p => new PoiDto(p.Name, p.Type.ToString(), p.X, p.Y, p.ConnectedTo))
            .ToList();

        return TypedResults.Ok(new SessionMapDto(pois, campaign.CurrentLocationName));
    }

    private static async Task<Results<Ok<SessionJournalDto>, NotFound>> GetSessionJournal(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        CancellationToken ct)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        var entries = campaign.JournalEntries
            .Select(e => new JournalEntryDto(e.Category.ToString(), e.Text, e.Day, e.Hour))
            .ToList();

        var fallen = campaign.FallenCharacters
            .Select(f => new FallenCharacterDto(f.Name, f.DayDied))
            .ToList();

        return TypedResults.Ok(new SessionJournalDto(entries, fallen));
    }

    private static async Task<Results<Ok<SessionStatusDto>, NotFound, ProblemHttpResult>> CreateSuccessor(
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
        if (!PlayerInput.TryCharacterName(request?.CharacterName, out var successorName, out var nameError))
            return ApiProblem.BadRequest(nameError);

        // Lock first, then read. Reading before acquiring the lock let a turn commit in between, so
        // every guard below could pass against state that was already stale.
        await using var lease = await sessionLock.TryAcquireAsync(sessionId, ct);
        if (lease is null)
            return ApiProblem.Conflict("GM response already in progress");

        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        if (campaign.WorldEnded || campaign.IsEnded)
            return ApiProblem.Conflict("This world has ended. Nothing walks it now.");

        var firstPlayerId = campaign.Players.FirstOrDefault();
        if (firstPlayerId == Guid.Empty)
            return ApiProblem.Conflict("No character to bury.");

        var character = await charactersRepo.Get(firstPlayerId, ct);
        if (character is null || !character.IsDead)
            return ApiProblem.Conflict("The wretch still breathes.");

        var fallenChronicleId = await chatHistoryRepo.GetActiveChronicle(campaign.Id, ct);
        var newChronicleId = await chatHistoryRepo.CreateSession(campaign.Id, ct);

        campaign.BuryCharacter(character.Id, character.Name);
        await campaignsRepo.SaveCampaign(campaign);

        // The successor is rolled here rather than by the narrator, so the campaign is never left in a
        // characterless state. Difficulty is the campaign's own — a death does not renegotiate it.
        var successorClass = request?.CharacterClass ?? characterCreationService.RollRandomClass();
        var successor = await characterCreationService.Create(
            successorName, campaign.Difficulty, successorClass);
        await campaignService.JoinCampaign(campaign.Id, successor.Id);

        if (fallenChronicleId is not null)
            await chatHistoryReducer.SeedEpitaphAsync(fallenChronicleId.Value, newChronicleId, ct);

        return TypedResults.Ok(new SessionStatusDto("in-progress"));
    }

    private static async Task<Results<Ok<SessionStatusDto>, NotFound, ProblemHttpResult>> AbandonSession(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        ISessionLock sessionLock,
        CancellationToken ct)
    {
        await using var lease = await sessionLock.TryAcquireAsync(sessionId, ct);
        if (lease is null)
            return ApiProblem.Conflict("GM response already in progress");

        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        if (!campaign.IsActive())
            return ApiProblem.Conflict("This campaign has already ended.");

        campaign.End();
        await campaignsRepo.SaveCampaign(campaign);
        return TypedResults.Ok(new SessionStatusDto("ended"));
    }

    private static async Task<Results<Ok<SessionMessagesDto>, NotFound>> GetSessionMessages(
        Guid sessionId,
        ICampaignsRepository campaignsRepo,
        IChatHistoryRepository chatHistoryRepo,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        var campaign = await campaignsRepo.GetOwned(sessionId, ct);
        if (campaign is null)
            return TypedResults.NotFound();

        (page, pageSize) = ClampPaging(page, pageSize);
        var chronicleId = await chatHistoryRepo.GetActiveChronicle(campaign.Id, ct);
        var (messages, totalMessages) = await LoadMessagePage(chatHistoryRepo, chronicleId, page, pageSize, ct);

        return TypedResults.Ok(new SessionMessagesDto(messages, totalMessages, page, pageSize));
    }

    private static (int Page, int PageSize) ClampPaging(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));

    /// <summary>Pages the campaign's active chronicle, in the database rather than in memory.</summary>
    private static async Task<(IReadOnlyList<ChatMessageDto> Messages, int Total)> LoadMessagePage(
        IChatHistoryRepository chatHistoryRepo, Guid? chronicleId, int page, int pageSize, CancellationToken ct)
    {
        if (chronicleId is null)
            return ([], 0);

        var loaded = await chatHistoryRepo.LoadSessionPage(
            chronicleId.Value, (page - 1) * pageSize, pageSize, ct);
        if (loaded is not { } chronicle)
            return ([], 0);

        var messages = chronicle.Messages
            .Select(m => new ChatMessageDto(m.Role.Value, m.Text, m.AuthorName))
            .ToList();

        return (messages, chronicle.Total);
    }
}
