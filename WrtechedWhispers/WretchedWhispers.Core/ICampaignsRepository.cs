namespace WretchedWhispers.Core;

public interface ICampaignsRepository
{
    public Task<Campaign?> Get(Guid campaignId);

    Task SaveCampaign(Campaign newCampaign);
}