using Microsoft.EntityFrameworkCore;
using Xunit;
using WretchedWhispers.Core;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class CampaignUserScopingTests : SqliteTestBase
{
    private readonly SqliteCampaignsRepository _repo;

    public CampaignUserScopingTests()
    {
        _repo = new SqliteCampaignsRepository(Db, JsonOptions, UserContext);
    }

    [Fact]
    public async Task GetForUser_ReturnsOnlyCampaignsBelongingToTheAmbientUser()
    {
        var campaignA = Campaign.Create(Difficulty.Grim, "Campaign A", "Test campaign A");
        var campaignB = Campaign.Create(Difficulty.Grim, "Campaign B", "Test campaign B");

        UserContext.SetUserId("user-A");
        await _repo.SaveCampaign(campaignA);
        UserContext.SetUserId("user-B");
        await _repo.SaveCampaign(campaignB);

        UserContext.SetUserId("user-A");
        var userACampaigns = await _repo.GetForUser(CancellationToken.None);

        Assert.Single(userACampaigns);
        Assert.Equal(campaignA.Id, userACampaigns[0].Id);
    }

    [Fact]
    public async Task SaveCampaign_StampsAmbientUserIdOnEntity()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Owned Campaign", "Test ownership stamp");

        UserContext.SetUserId("user-X");
        await _repo.SaveCampaign(campaign);

        var entity = await Db.Campaigns.FindAsync(campaign.Id);
        Assert.NotNull(entity);
        Assert.Equal("user-X", entity!.UserId);
    }

    [Fact]
    public async Task GetForUser_WithNoCampaigns_ReturnsEmptyList()
    {
        UserContext.SetUserId("user-C");
        var result = await _repo.GetForUser(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOwned_ReturnsNull_ForAnotherUsersCampaign()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Foreign", "Someone else's campaign");
        UserContext.SetUserId("user-A");
        await _repo.SaveCampaign(campaign);

        UserContext.SetUserId("user-B");
        var result = await _repo.GetOwned(campaign.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOwned_ReturnsCampaign_ForItsOwner()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Mine", "The ambient user's campaign");
        UserContext.SetUserId("user-A");
        await _repo.SaveCampaign(campaign);

        var result = await _repo.GetOwned(campaign.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(campaign.Id, result!.Id);
    }

    [Fact]
    public async Task SaveCampaign_UpdatesOwnerToTheCurrentAmbientUser()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Update Test", "Verify update propagation");

        UserContext.SetUserId("user-A");
        await _repo.SaveCampaign(campaign);

        UserContext.SetUserId("user-B");
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
        var campaign = Campaign.Create(Difficulty.Grim, "Race", "Concurrent turns");
        UserContext.SetUserId("user-A");
        await _repo.SaveCampaign(campaign);

        // Second request scope: its own context + change tracker over the same database.
        using var otherContext = CreateSeparateContext();
        var otherRepo = new SqliteCampaignsRepository(otherContext, JsonOptions, UserContext);

        // Both scopes load the row at the current Version BEFORE either commits.
        await otherContext.Campaigns.FindAsync(campaign.Id);

        // Turn 1 commits and rotates the token.
        await _repo.SaveCampaign(campaign);

        // Turn 2 commits against the now-stale token it loaded → conflict.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => otherRepo.SaveCampaign(campaign));
    }

    [Fact]
    public async Task SaveCampaign_ThrowsWhenUserContextNotSet()
    {
        // Use real UserContext (not the test stub) -- UserId not set
        var realUserContext = new UserContext();
        var repo = new SqliteCampaignsRepository(Db, JsonOptions, realUserContext);
        var campaign = Campaign.Create(Difficulty.Grim, "Throw Test", "Should throw");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.SaveCampaign(campaign));
    }
}
