using Xunit;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class CampaignMultiTenancyTests : SqliteTestBase
{
    private readonly SqliteCampaignsRepository _repo;

    public CampaignMultiTenancyTests()
    {
        _repo = new SqliteCampaignsRepository(Db, JsonOptions);
    }

    [Fact]
    public async Task GetForUser_ReturnsOnlyCampaignsBelongingToThatUser()
    {
        // Create and save campaigns for two different users
        var campaignA = Campaign.Create(DiceExpr.D6, "Campaign A", "Test campaign A");
        var campaignB = Campaign.Create(DiceExpr.D6, "Campaign B", "Test campaign B");

        await _repo.SaveCampaign(campaignA, "user-A");
        await _repo.SaveCampaign(campaignB, "user-B");

        // Act
        var userACampaigns = await _repo.GetForUser("user-A");

        // Assert
        Assert.Single(userACampaigns);
        Assert.Equal(campaignA.Id, userACampaigns[0].Id);
    }

    [Fact]
    public async Task SaveCampaign_WithUserId_SetsUserIdOnEntity()
    {
        var campaign = Campaign.Create(DiceExpr.D6, "Tenant Campaign", "Test tenant campaign");

        await _repo.SaveCampaign(campaign, "user-X");

        // Verify the entity has the correct UserId
        var entity = await Db.Campaigns.FindAsync(campaign.Id);
        Assert.NotNull(entity);
        Assert.Equal("user-X", entity!.UserId);
    }

    [Fact]
    public async Task GetForUser_WithNoCampaigns_ReturnsEmptyList()
    {
        var result = await _repo.GetForUser("user-C");

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
