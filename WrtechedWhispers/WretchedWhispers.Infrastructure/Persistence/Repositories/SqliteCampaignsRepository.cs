using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteCampaignsRepository(
    WretchedWhispersDbContext db,
    JsonSerializerOptions jsonOptions,
    ITenantContext tenantContext) : ICampaignsRepository
{
    public async Task<Campaign?> Get(Guid campaignId)
    {
        var entity = await db.Campaigns.FindAsync(campaignId);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Campaign>(entity.Data, jsonOptions);
    }

    public async Task SaveCampaign(Campaign newCampaign)
    {
        await SaveCampaign(newCampaign, tenantContext.UserId);
    }

    public async Task<List<Campaign>> GetForUser(string userId)
    {
        var entities = await db.Campaigns
            .Where(c => c.UserId == userId)
            .ToListAsync();

        return entities
            .Select(e => JsonSerializer.Deserialize<Campaign>(e.Data, jsonOptions)!)
            .ToList();
    }

    public async Task SaveCampaign(Campaign campaign, string userId)
    {
        var json = JsonSerializer.Serialize(campaign, jsonOptions);
        var entity = await db.Campaigns.FindAsync(campaign.Id);

        if (entity is not null)
        {
            entity.Data = json;
            entity.UserId = userId;
            // Rotate the concurrency token. EF matches the original (loaded) Version in the UPDATE's
            // WHERE clause; an overlapping turn that loaded the same original value will then commit
            // against 0 rows and throw DbUpdateConcurrencyException.
            entity.Version = Guid.NewGuid();
        }
        else
        {
            entity = new CampaignEntity { Id = campaign.Id, Data = json, UserId = userId, Version = Guid.NewGuid() };
            db.Campaigns.Add(entity);
        }

        await db.SaveChangesAsync();
    }
}
