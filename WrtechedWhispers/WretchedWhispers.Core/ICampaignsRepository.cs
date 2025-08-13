namespace WretchedWhispers.Core;

public interface ICampaignsRepository
{
    public Task<Campaign?> GetCampaignById(Guid campaignId);

    Task SaveCampaign(Campaign newCampaign);
}