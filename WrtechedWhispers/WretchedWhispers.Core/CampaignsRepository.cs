using System.Collections.Concurrent;

namespace WretchedWhispers.Core;

public class CampaignsRepository : ICampaignsRepository
{
    private static readonly ConcurrentDictionary<Guid, Campaign> Characters = new();

    public Task<Campaign?> Get(Guid campaignId)
    {
        Characters.TryGetValue(campaignId, out var campaign);
        return Task.FromResult(campaign);
    }

    public Task SaveCampaign(Campaign campaign)
    {
        Characters.AddOrUpdate(campaign.Id, campaign, (k, v) => v);

        return Task.CompletedTask;
    }
}