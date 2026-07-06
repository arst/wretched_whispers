using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class CampaignTests : TestBase
{
    [Fact]
    public void Create_ShouldCreateNewCampaignWithCorrectProperties()
    {
        // Arrange
        const string name = "Test Campaign";
        const string description = "A test campaign description";

        // Act
        var campaign = Campaign.Create(Difficulty.Grim, name, description);

        // Assert
        Assert.NotEqual(Guid.Empty, campaign.Id);
        Assert.Equal(name, campaign.Name);
        Assert.Equal(description, campaign.Description);
        Assert.Equal(1, campaign.CurrentDay);
        Assert.Equal(0, campaign.CurrentHour);
        Assert.Empty(campaign.Players);
        Assert.Empty(campaign.EncounterIds);
        Assert.Empty(campaign.Miseries);
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void JoinGame_ShouldAddPlayerToCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();

        // Act
        campaign.JoinGame(playerId);

        // Assert
        Assert.Single(campaign.Players);
        Assert.Contains(playerId, campaign.Players);
    }

    [Fact]
    public void JoinGame_ShouldAddMultiplePlayersToCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        // Act
        campaign.JoinGame(player1Id);
        campaign.JoinGame(player2Id);

        // Assert
        Assert.Equal(2, campaign.Players.Count);
        Assert.Contains(player1Id, campaign.Players);
        Assert.Contains(player2Id, campaign.Players);
    }

    [Fact]
    public void Start_ShouldStartCampaignWhenPlayersPresent()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);

        // Act
        campaign.Start();

        // Assert
        Assert.True(campaign.IsActive());
    }

    [Fact]
    public void Start_ShouldThrowExceptionWhenNoPlayers()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => campaign.Start());
        Assert.Equal("Cannot start a campaign without players.", exception.Message);
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void Start_ShouldThrowExceptionWhenAlreadyStarted()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => campaign.Start());
        Assert.Equal("Campaign is already started.", exception.Message);
    }

    [Fact]
    public void End_ShouldEndActiveCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();

        // Act
        campaign.End();

        // Assert
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void End_ShouldThrowExceptionWhenNotStarted()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => campaign.End());
        Assert.Equal("Campaign is not started yet.", exception.Message);
    }

    [Fact]
    public void IsActive_ShouldReturnFalseForNewCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        // Act & Assert
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void IsActive_ShouldReturnTrueForStartedCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();

        // Act & Assert
        Assert.True(campaign.IsActive());
    }

    [Fact]
    public void IsActive_ShouldReturnFalseForEndedCampaign()
    {
        // Arrange
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();
        campaign.End();

        // Act & Assert
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void Campaign_ShouldStartWithDay1Hour0()
    {
        // Arrange & Act
        var campaign = Campaign.Create(Difficulty.Grim, "Test Campaign", "Description");

        // Assert
        Assert.Equal(1, campaign.CurrentDay);
        Assert.Equal(0, campaign.CurrentHour);
    }

    [Fact]
    public void Campaign_ShouldHaveUniqueIds()
    {
        // Arrange & Act
        var campaign1 = Campaign.Create(Difficulty.Grim, "Campaign 1", "Description 1");
        var campaign2 = Campaign.Create(Difficulty.Grim, "Campaign 2", "Description 2");

        // Assert
        Assert.NotEqual(campaign1.Id, campaign2.Id);
    }
}