using WretchedWhispers.Core.Campaigns.Time;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

public class CampaignService(
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository,
    Dice dice)
{
    public async Task CreateCampaign(Difficulty difficulty, string name, string description, CancellationToken ct = default)
    {
        var campaign = Campaign.Create(difficulty, name, description);
        await campaignsRepository.SaveCampaign(campaign, ct);
    }

    private async Task<Campaign> GetRequiredCampaign(Guid campaignId, CancellationToken ct)
    {
        return await campaignsRepository.Get(campaignId, ct)
               ?? throw new InvalidOperationException($"Campaign with {campaignId} doesn't exist.");
    }

    public async Task<Campaign> ConfigureCampaign(Guid campaignId, string name, string description,
        PoiType startingPlaceType, string startingPlaceName, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.Configure(name, description, startingPlaceType, startingPlaceName);
        TryAutoStart(campaign);
        await campaignsRepository.SaveCampaign(campaign, ct);
        return campaign;
    }

    public async Task JoinCampaign(Guid campaignId, Guid characterId, CancellationToken ct = default)
    {
        var character = await charactersRepository.Get(characterId, ct);
        if (character is null) throw new InvalidOperationException($"Character with {characterId} doesn't exist.");

        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.JoinGame(character.Id);
        TryAutoStart(campaign);

        await campaignsRepository.SaveCampaign(campaign, ct);
    }

    // The campaign begins the moment it is configured AND has a player — a deterministic domain rule,
    // not a model decision. Order-independent.
    private static void TryAutoStart(Campaign campaign)
    {
        if (campaign is { IsConfigured: true, IsEnded: false } && campaign.Players.Count > 0 && !campaign.IsActive())
            campaign.Start();
    }

    public async Task<Campaign> RecordJournalEntry(Guid campaignId, JournalCategory category, string text, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.RecordJournalEntry(category, text);
        await campaignsRepository.SaveCampaign(campaign, ct);
        return campaign;
    }

    public async Task<Campaign> RecordPointOfInterest(Guid campaignId, PoiType type, string name, int x, int y, string? connectedTo, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.RecordPointOfInterest(type, name, x, y, connectedTo);
        await campaignsRepository.SaveCampaign(campaign, ct);
        return campaign;
    }

    public async Task<Campaign> SetPartyLocation(Guid campaignId, string name, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.SetPartyLocation(name);
        await campaignsRepository.SaveCampaign(campaign, ct);
        return campaign;
    }

    public async Task AttachEncounter(Guid campaignId, Guid encounterId, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        campaign.AddEncounter(encounterId);

        await campaignsRepository.SaveCampaign(campaign, ct);
    }

    public async Task EndCampaign(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);
        campaign.End();
        await campaignsRepository.SaveCampaign(campaign, ct);
    }

    public async Task<bool> IsActive(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        return campaign.IsActive();
    }

    public async Task<AdvanceTimeOutcome> AdvanceTime(Guid campaignId, int hours, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        var outcome = campaign.AdvanceTime(hours, dice);
        await campaignsRepository.SaveCampaign(campaign, ct);

        if (!outcome.IsNewDawn) return outcome;

        foreach (var playerId in campaign.Players)
        {
            var character = await charactersRepository.Get(playerId, ct);

            if (character is null) throw new InvalidOperationException("Player character not found.");

            character.StartNewDay(dice);
            await charactersRepository.Save(character, ct);
        }

        return outcome;
    }

    public async Task<AdvanceTimeOutcome> AdvanceTimeWithRest(Guid campaignId, int hours, CancellationToken ct = default)
    {
        var campaign = await GetRequiredCampaign(campaignId, ct);

        var outcome = campaign.AdvanceTime(hours, dice);
        await campaignsRepository.SaveCampaign(campaign, ct);

        // ponytail: one player to a campaign, so the result describes the last character rested. Report
        // per character when a campaign holds more than one.
        foreach (var playerId in campaign.Players)
        {
            var character = await charactersRepository.Get(playerId, ct);

            if (character is null) throw new InvalidOperationException("Player character not found.");
            var rest = character.Rest(hours, dice);

            if (outcome.IsNewDawn) character.StartNewDay(dice);

            await charactersRepository.Save(character, ct);
            outcome = outcome with
            {
                OmensRefreshed = rest.OmensRefreshed,
                HpHealed = rest.HpHealed,
                DamageTaken = rest.DamageTaken,
                CurrentHp = character.Hp.Current,
                NewInjury = rest.NewInjury?.DisplayName(),
                IsDead = rest.Died
            };
        }

        return outcome;
    }
}
