using Moq;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
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
        // Arrange
        var dawnDice = new DiceExpr(1, 6);
        var name = "Test Campaign";
        var description = "A test campaign";

        // Act
        await _service.CreateCampaign(dawnDice, name, description);

        // Assert
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task JoinCampaign_WithValidIds_ShouldSaveCampaign()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        var character = CreateTestCharacter(characterId);

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        // Act
        await _service.JoinCampaign(campaignId, characterId);

        // Assert
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
        Assert.Contains(characterId, campaign.Players);
    }

    [Fact]
    public async Task JoinCampaign_WithInvalidCharacterId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Character?)null);

        // Act & Assert
        try
        {
            await _service.JoinCampaign(campaignId, characterId);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Character with {characterId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task JoinCampaign_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var character = CreateTestCharacter(characterId);

        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.JoinCampaign(campaignId, characterId);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task StartCampaign_WithValidCampaign_ShouldStartCampaign()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        var characterId = Guid.NewGuid();

        campaign.JoinGame(characterId);

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        await _service.StartCampaign(campaignId);

        // Assert
        Assert.True(campaign.IsActive());
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task StartCampaign_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.StartCampaign(campaignId);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task EndCampaign_WithValidCampaign_ShouldEndCampaign()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        var characterId = Guid.NewGuid();

        campaign.JoinGame(characterId);
        campaign.Start();

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        await _service.EndCampaign(campaignId);

        // Assert
        Assert.False(campaign.IsActive());
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task EndCampaign_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.EndCampaign(campaignId);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task IsActive_WithActiveCampaign_ShouldReturnTrue()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        var characterId = Guid.NewGuid();

        campaign.JoinGame(characterId);
        campaign.Start();

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        var result = await _service.IsActive(campaignId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsActive_WithInactiveCampaign_ShouldReturnFalse()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        var result = await _service.IsActive(campaignId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsActive_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.IsActive(campaignId);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task AdvanceTime_WithValidCampaign_ShouldReturnOutcome()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        const int hours = 5;

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);

        // Act
        var result = await _service.AdvanceTime(campaignId, hours);

        // Assert
        Assert.True(result != null);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceTimeWithRest_WithValidCampaign_ShouldReturnOutcome()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var campaign = Campaign.Create(new DiceExpr(1, 6), "Test Campaign", "Description");
        var character = CreateTestCharacter(characterId);
        const int hours = 8;

        campaign.JoinGame(characterId);

        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync(campaign);
        _charactersRepository.Setup(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(character);

        // Act
        var result = await _service.AdvanceTimeWithRest(campaignId, hours);

        // Assert
        Assert.True(result != null);
        _charactersRepository.Verify(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()), Times.Once);
        _campaignsRepository.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceTime_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.AdvanceTime(campaignId, 5);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    [Fact]
    public async Task AdvanceTimeWithRest_WithInvalidCampaignId_ShouldThrowArgumentException()
    {
        // Arrange
        var campaignId = Guid.NewGuid();
        _campaignsRepository.Setup(r => r.Get(It.IsAny<Guid>())).ReturnsAsync((Campaign?)null);

        // Act & Assert
        try
        {
            await _service.AdvanceTimeWithRest(campaignId, 8);
            Assert.Fail("Expected ArgumentException was not thrown");
        }
        catch (ArgumentException ex)
        {
            Assert.Contains($"Campaign with {campaignId} doesn't exist", ex.Message);
        }
    }

    /// <summary>
    ///     Helper method to create a test character with minimal required dependencies.
    /// </summary>
    private Character CreateTestCharacter(Guid characterId)
    {
        var abilities = new Abilities(
            new AbilityScore(0), // Agility
            new AbilityScore(0), // Presence  
            new AbilityScore(0), // Strength
            new AbilityScore(0) // Toughness
        );

        var equipment = new StartingEquipment(
            0,
            1,
            "Sack",
            null,
            null,
            Weapon.Create(WeaponKind.Sword),
            new Armor(NoArmorTier.Instance),
            null,
            []
        );

        return Character.Create(characterId, "Test Character", 10, abilities, equipment, Dice);
    }
}