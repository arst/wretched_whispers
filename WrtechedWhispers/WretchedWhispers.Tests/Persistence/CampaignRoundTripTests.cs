using Xunit;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class CampaignRoundTripTests : TestBase
{
    private readonly SqliteTestBase _db;
    private readonly SqliteCampaignsRepository _repo;

    public CampaignRoundTripTests()
    {
        _db = new SqliteTestBase();
        _repo = new SqliteCampaignsRepository(_db.Db, _db.JsonOptions, _db.TenantContext);
    }

    public override void Dispose()
    {
        _db.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task Save_Then_Get_ReturnsCampaignWithMatchingState()
    {
        var campaign = Campaign.Create(DiceExpr.D6, "DoomCampaign", "The end is nigh");
        var charId = Guid.NewGuid();
        campaign.JoinGame(charId);

        await _repo.SaveCampaign(campaign);
        var loaded = await _repo.Get(campaign.Id);

        Assert.NotNull(loaded);
        Assert.Equal(campaign.Id, loaded.Id);
        Assert.Equal(campaign.Name, loaded.Name);
        Assert.Equal(campaign.Description, loaded.Description);
        Assert.Equal(campaign.CurrentDay, loaded.CurrentDay);
        Assert.Equal(campaign.CurrentHour, loaded.CurrentHour);
        Assert.Contains(charId, loaded.Players);
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var loaded = await _repo.Get(Guid.NewGuid());
        Assert.Null(loaded);
    }
}
