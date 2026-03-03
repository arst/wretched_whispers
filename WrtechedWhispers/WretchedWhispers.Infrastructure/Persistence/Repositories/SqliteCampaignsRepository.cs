using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Infrastructure.Persistence.Repositories;

public class SqliteCampaignsRepository : ICampaignsRepository
{
    private readonly WretchedWhispersDbContext _db;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteCampaignsRepository(WretchedWhispersDbContext db, JsonSerializerOptions jsonOptions)
    {
        _db = db;
        _jsonOptions = jsonOptions;
    }

    public async Task<Campaign?> Get(Guid campaignId)
    {
        var entity = await _db.Campaigns.FindAsync(campaignId);
        if (entity is null) return null;

        return JsonSerializer.Deserialize<Campaign>(entity.Data, _jsonOptions);
    }

    public async Task SaveCampaign(Campaign newCampaign)
    {
        var json = JsonSerializer.Serialize(newCampaign, _jsonOptions);
        var entity = await _db.Campaigns.FindAsync(newCampaign.Id);

        if (entity is not null)
        {
            entity.Data = json;
        }
        else
        {
            entity = new CampaignEntity { Id = newCampaign.Id, Data = json };
            _db.Campaigns.Add(entity);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<Campaign>> GetForUser(string userId)
    {
        var entities = await _db.Campaigns
            .Where(c => c.UserId == userId)
            .ToListAsync();

        return entities
            .Select(e => JsonSerializer.Deserialize<Campaign>(e.Data, _jsonOptions)!)
            .ToList();
    }

    public async Task SaveCampaign(Campaign campaign, string userId)
    {
        var json = JsonSerializer.Serialize(campaign, _jsonOptions);
        var entity = await _db.Campaigns.FindAsync(campaign.Id);

        if (entity is not null)
        {
            entity.Data = json;
            entity.UserId = userId;
        }
        else
        {
            entity = new CampaignEntity { Id = campaign.Id, Data = json, UserId = userId };
            _db.Campaigns.Add(entity);
        }

        await _db.SaveChangesAsync();
    }
}
