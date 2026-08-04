using System.Text.Json;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

public class CampaignTests
{
    private static readonly JsonSerializerOptions Options = AggregateJsonOptions.Create();

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
        Assert.Throws<InvalidOperationException>(() => campaign.Start());
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
        Assert.Throws<InvalidOperationException>(() => campaign.Start());
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
        Assert.Throws<InvalidOperationException>(() => campaign.End());
    }

    [Fact]
    public void Deserializing_a_blob_without_difficulty_defaults_to_grim()
    {
        // Simulate a pre-feature persisted campaign: serialize, then strip the Difficulty property.
        var campaign = Campaign.Create(Difficulty.Doomed, "Legacy", "desc");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(campaign, Options));
        Assert.NotNull(node); // xUnit NotNull narrows nullability — avoids the null-forgiving operator
        node.AsObject().Remove("difficulty"); // AggregateJsonOptions uses camelCase property naming

        var restored = JsonSerializer.Deserialize<Campaign>(node.ToJsonString(), Options);
        Assert.NotNull(restored);
        Assert.Equal(Difficulty.Grim, restored.Difficulty);
    }
}
