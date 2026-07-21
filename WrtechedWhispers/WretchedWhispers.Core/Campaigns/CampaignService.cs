using WretchedWhispers.Core.Campaigns.Time;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

public class CampaignService(
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository,
    Dice dice)
{
    public async Task CreateCampaign(Difficulty difficulty, string name, string description)
    {
        var campaign = Campaign.Create(difficulty, name, description);
        await campaignsRepository.SaveCampaign(campaign);
    }

    public async Task<Campaign> ConfigureCampaign(Guid campaignId, string name, string description)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.Configure(name, description);
        TryAutoStart(campaign);
        await campaignsRepository.SaveCampaign(campaign);
        return campaign;
    }

    public async Task JoinCampaign(Guid campaignId, Guid characterId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character is null) throw new ArgumentException($"Character with {characterId} doesn't exist.");

        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.JoinGame(character.Id);
        TryAutoStart(campaign);

        await campaignsRepository.SaveCampaign(campaign);
    }

    // The campaign begins the moment it is configured AND has a player — a deterministic domain rule,
    // not a model decision. Order-independent.
    private static void TryAutoStart(Campaign campaign)
    {
        if (campaign is { IsConfigured: true, IsEnded: false } && campaign.Players.Count > 0 && !campaign.IsActive())
            campaign.Start();
    }

    public async Task<Campaign> RecordJournalEntry(Guid campaignId, JournalCategory category, string text)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.RecordJournalEntry(category, text);
        await campaignsRepository.SaveCampaign(campaign);
        return campaign;
    }

    public async Task<Campaign> RecordPointOfInterest(Guid campaignId, PoiType type, string name, int x, int y, string? connectedTo)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.RecordPointOfInterest(type, name, x, y, connectedTo);
        await campaignsRepository.SaveCampaign(campaign);
        return campaign;
    }

    public async Task<Campaign> SetPartyLocation(Guid campaignId, string name)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.SetPartyLocation(name);
        await campaignsRepository.SaveCampaign(campaign);
        return campaign;
    }

    public async Task AttachEncounter(Guid campaignId, Guid encounterId)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.AddEncounter(encounterId);

        await campaignsRepository.SaveCampaign(campaign);
    }

    public async Task EndCampaign(Guid campaignId)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");
        campaign.End();
        await campaignsRepository.SaveCampaign(campaign);
    }

    public async Task<bool> IsActive(Guid campaignId)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        return campaign.IsActive();
    }

    public async Task<AdvanceTimeOutcome> AdvanceTime(Guid campaignId, int hours)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        var outcome = campaign.AdvanceTime(hours, dice);
        await campaignsRepository.SaveCampaign(campaign);

        if (!outcome.IsNewDawn) return outcome;

        foreach (var playerId in campaign.Players)
        {
            var character = await charactersRepository.Get(playerId);

            if (character is null) throw new InvalidOperationException("Player character not found.");

            character.StartNewDay(dice);
            await charactersRepository.Save(character);
        }

        return outcome;
    }

    public async Task<AdvanceTimeOutcome> AdvanceTimeWithRest(Guid campaignId, int hours)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        var outcome = campaign.AdvanceTime(hours, dice);
        await campaignsRepository.SaveCampaign(campaign);

        foreach (var playerId in campaign.Players)
        {
            var character = await charactersRepository.Get(playerId);

            if (character is null) throw new InvalidOperationException("Player character not found.");
            character.Rest(hours, dice);

            if (outcome.IsNewDawn) character.StartNewDay(dice);

            await charactersRepository.Save(character);
        }

        return outcome;
    }
}
