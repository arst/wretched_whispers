using Microsoft.Extensions.Logging;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Engine.Services;

public sealed class SessionContextLoader(
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository,
    IEncountersRepository encountersRepository,
    ILogger<SessionContextLoader> logger) : ISessionContextLoader
{
    public async Task<SessionContext> LoadAsync(Guid sessionId, CancellationToken ct = default)
    {
        var campaign = await campaignsRepository.Get(sessionId);
        if (campaign is null)
        {
            logger.LogInformation("Session {SessionId}: no campaign found", sessionId);
            return new SessionContext { SessionId = sessionId };
        }

        return await BuildAsync(sessionId, campaign, ct);
    }

    public async Task<SessionContext?> LoadOwnedAsync(Guid sessionId, CancellationToken ct = default)
    {
        var campaign = await campaignsRepository.GetOwned(sessionId, ct);
        return campaign is null ? null : await BuildAsync(sessionId, campaign, ct);
    }

    private async Task<SessionContext> BuildAsync(Guid sessionId, Campaign campaign, CancellationToken ct)
    {
        var context = new SessionContext { SessionId = sessionId };

        context.Campaign = campaign;
        context.SetCampaignId(campaign.Id);

        var firstPlayerId = campaign.Players.FirstOrDefault();
        if (firstPlayerId != Guid.Empty)
        {
            var character = await charactersRepository.Get(firstPlayerId, ct);
            if (character is not null)
            {
                context.Character = character;
                context.SetCharacterId(character.Id);
            }
        }

        foreach (var encId in campaign.EncounterIds.Reverse())
        {
            var enc = await encountersRepository.Get(encId);
            if (enc is not null && enc.IsStarted && !enc.IsResolved)
            {
                context.ActiveEncounter = enc;
                context.SetActiveEncounterId(enc.Id);
                break;
            }
        }

        var stage = context.DeriveStage();
        logger.LogInformation(
            "Session {SessionId}: loaded context — Stage={Stage}, CharacterId={CharacterId}, CampaignId={CampaignId}, EncounterId={EncounterId}",
            sessionId, stage, context.CharacterId, context.CampaignId, context.ActiveEncounterId);

        return context;
    }
}
