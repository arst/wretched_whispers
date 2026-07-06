using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public sealed class CampaignAutoStartTests : TestBase
{
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();

    private CampaignService CreateService() =>
        new(_campaignsRepo.Object, _charactersRepo.Object, Dice);

    [Fact]
    public async Task Configure_ThenJoin_AutoStarts()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = CreateService();

        await service.ConfigureCampaign(campaign.Id, DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.False(campaign.IsActive()); // configured but no player yet

        await service.JoinCampaign(campaign.Id, character.Id);
        Assert.True(campaign.IsActive()); // both conditions met -> started
    }

    [Fact]
    public async Task Join_ThenConfigure_AutoStarts()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var service = CreateService();

        await service.JoinCampaign(campaign.Id, character.Id);
        Assert.False(campaign.IsActive()); // player joined, not configured yet

        await service.ConfigureCampaign(campaign.Id, DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.True(campaign.IsActive());
    }

    [Fact]
    public void Configure_SetsIsConfigured()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "raw", "raw");
        Assert.False(campaign.IsConfigured);
        campaign.Configure(DiceExpr.Parse("d20"), "Doom", "The end");
        Assert.True(campaign.IsConfigured);
    }
}
