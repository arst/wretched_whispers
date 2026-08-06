using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class CampaignServiceTests : TestBase
{
    private readonly Mock<ICampaignsRepository> _campaignsRepository;
    private readonly Mock<ICharactersRepository> _charactersRepository;
    private readonly CampaignService _service;

    public CampaignServiceTests()
    {
        _campaignsRepository = new Mock<ICampaignsRepository>();
        _charactersRepository = new Mock<ICharactersRepository>();
        _service = new CampaignService(_campaignsRepository.Object, _charactersRepository.Object, Dice);
    }

    [Fact]
    public async Task CreateCampaign_ShouldSaveCampaign()
    {
        await _service.CreateCampaign(Difficulty.Grim, "Test Campaign", "A test campaign");

        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task JoinCampaign_WithValidIds_ShouldSaveCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice);

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        // Act
        await _service.JoinCampaign(Guid.NewGuid(), character.Id);

        // Assert
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
        Assert.Contains(character.Id, campaign.Players);
    }

    [Fact]
    public async Task JoinCampaign_WithInvalidCharacterId_ThrowsArgumentException()
    {
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Character?)null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.JoinCampaign(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task AttachEncounter_WithValidCampaign_AddsEncounterAndSaves()
    {
        // Arrange
        var encounterId = Guid.NewGuid();
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        await _service.AttachEncounter(Guid.NewGuid(), encounterId);

        // Assert
        Assert.Contains(encounterId, campaign.EncounterIds);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Theory]
    [InlineData(nameof(CampaignService.JoinCampaign))]
    [InlineData(nameof(CampaignService.ConfigureCampaign))]
    [InlineData(nameof(CampaignService.AttachEncounter))]
    [InlineData(nameof(CampaignService.EndCampaign))]
    [InlineData(nameof(CampaignService.IsActive))]
    [InlineData(nameof(CampaignService.AdvanceTime))]
    [InlineData(nameof(CampaignService.AdvanceTimeWithRest))]
    public async Task AnyServiceCall_WithMissingCampaign_ThrowsArgumentException(string method)
    {
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);
        // JoinCampaign validates the character before the campaign, so give it a real one.
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCharacters.Create(Dice));

        Task Act() => method switch
        {
            nameof(CampaignService.JoinCampaign) => _service.JoinCampaign(Guid.NewGuid(), Guid.NewGuid()),
            nameof(CampaignService.ConfigureCampaign) => _service.ConfigureCampaign(Guid.NewGuid(), "Doom", "The end"),
            nameof(CampaignService.AttachEncounter) => _service.AttachEncounter(Guid.NewGuid(), Guid.NewGuid()),
            nameof(CampaignService.EndCampaign) => _service.EndCampaign(Guid.NewGuid()),
            nameof(CampaignService.IsActive) => _service.IsActive(Guid.NewGuid()),
            nameof(CampaignService.AdvanceTime) => _service.AdvanceTime(Guid.NewGuid(), 5),
            nameof(CampaignService.AdvanceTimeWithRest) => _service.AdvanceTimeWithRest(Guid.NewGuid(), 8),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        await Assert.ThrowsAsync<ArgumentException>(Act);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Never);
    }

    [Fact]
    public async Task Configure_ThenJoin_AutoStarts()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepository.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        await _service.ConfigureCampaign(campaign.Id, "Doom", "The end");
        Assert.False(campaign.IsActive()); // configured but no player yet

        await _service.JoinCampaign(campaign.Id, character.Id);
        Assert.True(campaign.IsActive()); // both conditions met -> started
    }

    [Fact]
    public async Task Join_ThenConfigure_AutoStarts()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepository.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        await _service.JoinCampaign(campaign.Id, character.Id);
        Assert.False(campaign.IsActive()); // player joined, not configured yet

        await _service.ConfigureCampaign(campaign.Id, "Doom", "The end");
        Assert.True(campaign.IsActive());
    }

    [Fact]
    public async Task EndCampaign_WithValidCampaign_ShouldEndCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        campaign.JoinGame(Guid.NewGuid());
        campaign.Start();

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        await _service.EndCampaign(Guid.NewGuid());

        // Assert
        Assert.False(campaign.IsActive());
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task IsActive_WithActiveCampaign_ShouldReturnTrue()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        campaign.JoinGame(Guid.NewGuid());
        campaign.Start();

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act & Assert
        Assert.True(await _service.IsActive(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsActive_WithInactiveCampaign_ShouldReturnFalse()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act & Assert
        Assert.False(await _service.IsActive(Guid.NewGuid()));
    }

    [Fact]
    public async Task AdvanceTime_WithValidCampaign_AdvancesClock()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        var result = await _service.AdvanceTime(Guid.NewGuid(), 5);

        // Assert
        Assert.Equal(1, campaign.CurrentDay);
        Assert.Equal(5, campaign.CurrentHour);
        Assert.False(result.IsNewDawn);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceTime_AcrossTwoDawns_RollsTheDoomClockForEach()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        // Two dawns crossed, both trigger (dawn roll 1), each picking a distinct misery
        // (d6*10+d6 indices 11 and 12). One roll per dawn — a 48-hour span used to tick once.
        SetupDiceRolls(0, 0, 0, 0, 0, 1);

        var result = await _service.AdvanceTime(Guid.NewGuid(), 48);

        Assert.Equal(3, campaign.CurrentDay);
        Assert.Equal(0, campaign.CurrentHour);
        Assert.True(result.IsNewDawn);
        Assert.Equal(2, result.Miseries.Count);
    }

    [Fact]
    public async Task AdvanceTimeWithRest_FullNightRest_AdvancesClockAndRefreshesOmens()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice); // 0 omens -> a full night's rest refills d2

        campaign.JoinGame(character.Id);

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);
        SetupDiceRolls(3, 1); // rest heal d6 = 4, omen refill d2 = 2

        // Act
        var result = await _service.AdvanceTimeWithRest(Guid.NewGuid(), 8);

        // Assert
        Assert.Equal(8, campaign.CurrentHour);
        Assert.False(result.IsNewDawn);
        Assert.Equal(2, result.OmensRefreshed);
        _charactersRepository.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }
}
