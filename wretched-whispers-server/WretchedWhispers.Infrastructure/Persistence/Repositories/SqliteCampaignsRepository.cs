using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteCampaignsRepository(
    WretchedWhispersDbContext db,
    JsonSerializerOptions jsonOptions,
    IUserContext userContext) : ICampaignsRepository
{
    public async Task<Campaign?> Get(Guid campaignId)
    {
        var entity = await db.Campaigns.FindAsync(campaignId);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Campaign>(entity.Data, jsonOptions);
    }

    public async Task<Campaign?> GetOwned(Guid campaignId, CancellationToken ct)
    {
        var entity = await db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.UserId == userContext.UserId, ct);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Campaign>(entity.Data, jsonOptions);
    }

    public async Task<List<Campaign>> GetForUser(CancellationToken ct)
    {
        var entities = await db.Campaigns
            .Where(c => c.UserId == userContext.UserId)
            .ToListAsync(ct);

        return entities
            .Select(e => JsonSerializer.Deserialize<Campaign>(e.Data, jsonOptions)!)
            .ToList();
    }

    public async Task SaveCampaign(Campaign campaign)
    {
        var userId = userContext.UserId;
        var json = JsonSerializer.Serialize(campaign, jsonOptions);
        var entity = await db.Campaigns.FindAsync(campaign.Id);

        if (entity is not null)
        {
            if (entity.UserId != userId)
                throw new InvalidOperationException(
                    $"Campaign {campaign.Id} belongs to another user; refusing to reassign ownership.");

            entity.Data = json;
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
