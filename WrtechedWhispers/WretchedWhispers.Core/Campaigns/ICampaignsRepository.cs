namespace WretchedWhispers.Core.Campaigns;

public interface ICampaignsRepository
{
    Task<Campaign?> Get(Guid campaignId);

    Task SaveCampaign(Campaign newCampaign);

    // Multi-tenant methods for API layer
    Task<List<Campaign>> GetForUser(string userId);

    Task SaveCampaign(Campaign campaign, string userId);
}