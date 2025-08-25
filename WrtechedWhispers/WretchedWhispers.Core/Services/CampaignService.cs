using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Services;

public class CampaignService(
    IRandomService rng,
    ICampaignsRepository campaignsRepository,
    ICharactersRepository charactersRepository)
{
    public async Task CreateCampaign(DiceExpr dawnDice, string name, string description)
    {
        var campaign = Campaign.Create(dawnDice, name, description);
        await campaignsRepository.SaveCampaign(campaign);
    }

    public async Task JoinCampaign(Guid campaignId, Guid characterId)
    {
        var character = await charactersRepository.Get(characterId);
        if (character is null) throw new ArgumentException($"Character with {characterId} doesn't exist.");

        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");

        campaign.JoinGame(character.Id);

        await campaignsRepository.SaveCampaign(campaign);
    }

    public async Task StartCampaign(Guid campaignId)
    {
        var campaign = await campaignsRepository.Get(campaignId);
        if (campaign is null) throw new ArgumentException($"Campaign with {campaignId} doesn't exist.");
        campaign.Start();
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

        var outcome = campaign.AdvanceTime(hours, rng);
        await campaignsRepository.SaveCampaign(campaign);

        foreach (var playerId in campaign.Players)
        {
            var character = await charactersRepository.Get(playerId);

            if (character is null) throw new InvalidOperationException("Player character not found.");

            character.NewDawn();
            await charactersRepository.Save(character);
        }

        return outcome;
    }
}