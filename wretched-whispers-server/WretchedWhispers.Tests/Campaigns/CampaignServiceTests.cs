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
    public async Task JoinCampaign_WithInvalidCharacterId_ThrowsInvalidOperationException()
    {
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Character?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
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
    public async Task AnyServiceCall_WithMissingCampaign_ThrowsInvalidOperationException(string method)
    {
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);
        // JoinCampaign validates the character before the campaign, so give it a real one.
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestCharacters.Create(Dice));

        Task Act() => method switch
        {
            nameof(CampaignService.JoinCampaign) => _service.JoinCampaign(Guid.NewGuid(), Guid.NewGuid()),
            nameof(CampaignService.ConfigureCampaign) => _service.ConfigureCampaign(Guid.NewGuid(), "Doom", "The end", PoiType.Town, "Galgenbeck"),
            nameof(CampaignService.AttachEncounter) => _service.AttachEncounter(Guid.NewGuid(), Guid.NewGuid()),
            nameof(CampaignService.EndCampaign) => _service.EndCampaign(Guid.NewGuid()),
            nameof(CampaignService.IsActive) => _service.IsActive(Guid.NewGuid()),
            nameof(CampaignService.AdvanceTime) => _service.AdvanceTime(Guid.NewGuid(), 5),
            nameof(CampaignService.AdvanceTimeWithRest) => _service.AdvanceTimeWithRest(Guid.NewGuid(), 8),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Never);
    }

    [Fact]
    public async Task Configure_ThenJoin_AutoStarts()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "raw", "raw");
        var character = TestCharacters.Create(Dice);
        _campaignsRepository.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);

        await _service.ConfigureCampaign(campaign.Id, "Doom", "The end", PoiType.Town, "Galgenbeck");
        Assert.False(campaign.IsActive()); // configured but no player yet

        await _service.JoinCampaign(campaign.Id, character.Id);
        Assert.True(campaign.IsActive()); // both conditions met -> started
    }

    [Fact]
    public async Task Configure_ChartsTheStartingPlaceAndPutsThePartyThere()
    {
        // Six of eleven simulated games never had a place on the sheet: the opening turn describes where
        // the character wakes, and no tool offered on that turn could record it.
        var campaign = Campaign.Create(Difficulty.Grim, "raw", "raw");
        _campaignsRepository.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        await _service.ConfigureCampaign(campaign.Id, "Doom", "The end", PoiType.Ruin, "The Drowned Chapel");

        var place = Assert.Single(campaign.Pois);
        Assert.Equal(("The Drowned Chapel", PoiType.Ruin), (place.Name, place.Type));
        Assert.Equal("The Drowned Chapel", campaign.CurrentLocationName);
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

        await _service.ConfigureCampaign(campaign.Id, "Doom", "The end", PoiType.Town, "Galgenbeck");
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

    // The tool hands this result to the narrator as it is: what it does not carry cannot be told.
    [Fact]
    public async Task AdvanceTimeWithRest_WhileInfected_ReportsTheDamageAndTheHpLeft()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice, maxHp: 20);
        character.Infect();
        campaign.JoinGame(character.Id);
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);
        SetupDiceRolls(5); // infection d6 = 6

        var result = await _service.AdvanceTimeWithRest(Guid.NewGuid(), 8);

        Assert.Equal(6, result.DamageTaken);
        Assert.Equal(0, result.HpHealed);
        Assert.Equal(14, result.CurrentHp);
        Assert.Null(result.NewInjury);
        Assert.False(result.IsDead);
    }

    [Fact]
    public async Task AdvanceTimeWithRest_ThatBreaksTheCharacter_NamesTheInjury()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice, maxHp: 1);
        character.Infect();
        campaign.JoinGame(character.Id);
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);
        SetupDiceRolls(5, 2, 5, 2); // infection d6 = 6; Broken d4 = 3: survives; injury d6 = 6: lost eye; back up d4

        var result = await _service.AdvanceTimeWithRest(Guid.NewGuid(), 8);

        Assert.Equal("lost eye", result.NewInjury);
        Assert.False(result.IsDead);
    }

    [Fact]
    public async Task AdvanceTimeWithRest_ThatHeals_ReportsTheHpHealedAndTheHpLeft()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice, maxHp: 20, startingOmens: 1);
        character.Infect();
        SetupDiceRolls(5, 2); // the infection takes 6; then a short rest heals d4 = 3
        character.Rest(8, Dice);
        character.CureInfection();
        campaign.JoinGame(character.Id);
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        var result = await _service.AdvanceTimeWithRest(Guid.NewGuid(), 4);

        Assert.Equal(3, result.HpHealed);
        Assert.Equal(17, result.CurrentHp);
        Assert.Equal(0, result.DamageTaken);
        Assert.False(result.IsDead);
    }

    [Fact]
    public async Task AdvanceTimeWithRest_ThatKills_ReportsTheDeath()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var character = TestCharacters.Create(Dice, maxHp: 1);
        character.Infect();
        campaign.JoinGame(character.Id);
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);
        SetupDiceRolls(5, 0); // infection d6 = 6; Broken d4 = 1: dead

        var result = await _service.AdvanceTimeWithRest(Guid.NewGuid(), 8);

        Assert.True(result.IsDead);
        Assert.Equal(0, result.CurrentHp);
        Assert.Null(result.NewInjury);
    }
}
