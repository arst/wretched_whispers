using Microsoft.EntityFrameworkCore;
using Xunit;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class CampaignMultiTenancyTests : SqliteTestBase
{
    private readonly SqliteCampaignsRepository _repo;

    public CampaignMultiTenancyTests()
    {
        _repo = new SqliteCampaignsRepository(Db, JsonOptions, TenantContext);
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

    [Fact]
    public async Task ParameterlessSaveCampaign_UsesITenantContextUserId()
    {
        TenantContext.SetUserId("tenant-user-1");
        var campaign = Campaign.Create(DiceExpr.D6, "Tenant Test", "Verify tenant propagation");

        await _repo.SaveCampaign(campaign);

        var entity = await Db.Campaigns.FindAsync(campaign.Id);
        Assert.NotNull(entity);
        Assert.Equal("tenant-user-1", entity!.UserId);
    }

    [Fact]
    public async Task ParameterlessSaveCampaign_UpdatesExistingCampaignUserId()
    {
        var campaign = Campaign.Create(DiceExpr.D6, "Update Test", "Verify update propagation");

        // Save with explicit userId "user-A"
        await _repo.SaveCampaign(campaign, "user-A");

        // Set tenant context to "user-B" and save via parameterless overload
        TenantContext.SetUserId("user-B");
        await _repo.SaveCampaign(campaign);

        var entity = await Db.Campaigns.FindAsync(campaign.Id);
        Assert.NotNull(entity);
        Assert.Equal("user-B", entity!.UserId);
    }

    [Fact]
    public async Task SaveCampaign_WhenAnotherTurnCommittedConcurrently_ThrowsConcurrencyException()
    {
        // Two overlapping turns on the same session (e.g. a double-submit): both load the campaign at
        // the same Version, both try to commit. The optimistic-concurrency token must let the first
        // win and make the second throw — the cross-instance backstop the in-memory guard can't give.
        var campaign = Campaign.Create(DiceExpr.D6, "Race", "Concurrent turns");
        await _repo.SaveCampaign(campaign, "user-A");

        // Second request scope: its own context + change tracker over the same database.
        using var otherContext = CreateSeparateContext();
        var otherRepo = new SqliteCampaignsRepository(otherContext, JsonOptions, TenantContext);

        // Both scopes load the row at the current Version BEFORE either commits.
        await otherContext.Campaigns.FindAsync(campaign.Id);

        // Turn 1 commits and rotates the token.
        await _repo.SaveCampaign(campaign, "user-A");

        // Turn 2 commits against the now-stale token it loaded → conflict.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => otherRepo.SaveCampaign(campaign, "user-A"));
    }

    [Fact]
    public async Task ParameterlessSaveCampaign_ThrowsWhenTenantContextNotSet()
    {
        // Use real TenantContext (not StubTenantContext) -- UserId not set
        var realTenantContext = new TenantContext();
        var repo = new SqliteCampaignsRepository(Db, JsonOptions, realTenantContext);
        var campaign = Campaign.Create(DiceExpr.D6, "Throw Test", "Should throw");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.SaveCampaign(campaign));
    }
}
