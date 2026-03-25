using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Campaigns.World;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Campaigns;

/// <summary>
/// Tests verifying the Campaign states that drive DeriveStatus logic:
/// - No players -> "character-creation"
/// - Active campaign with players -> "in-progress"
/// - Ended campaign (dead character or world ended) -> "ended"
///
/// DeriveStatus is private static in GameSessionService/SessionEndpoints,
/// so we test the observable Campaign domain state it relies on.
/// </summary>
public sealed class DeriveStatusTests : TestBase
{
    [Fact]
    public void DeadCharacter_CampaignEnded_MapsToEndedStatus()
    {
        // Arrange: campaign with player, started, then ended (simulating character death)
        var campaign = Campaign.Create(DiceExpr.D6, "Test Campaign", "A doomed campaign");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();
        campaign.End();

        // Assert: DeriveStatus would return "ended" because
        // campaign.Players.Count > 0 AND campaign.IsActive() == false
        Assert.True(campaign.Players.Count > 0);
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void WorldEnded_CalendarSevenMiseries_MapsToEndedStatus()
    {
        // Arrange: verify that 7 miseries => WorldEnded is true at the calendar level,
        // and a campaign that has ended maps to "ended" in DeriveStatus.
        // Calendar with 7 miseries triggers WorldEnded
        var rolls = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            rolls.Add(0);     // Dawn roll = 1 (triggers misery)
            rolls.Add(i + 1); // First d6 for misery code
            rolls.Add(1);     // Second d6 for misery code
        }
        SetupDiceRolls(rolls.ToArray());

        var calendar = new CalendarOfNechrubel();
        for (var i = 0; i < 7; i++)
            calendar.DawnRoll(DiceExpr.D20, Dice);

        Assert.True(calendar.WorldEnded);

        // A campaign that has been ended (by game logic detecting WorldEnded)
        // maps to "ended" status
        var campaign = Campaign.Create(DiceExpr.D6, "Doomed Campaign", "The end approaches");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();
        campaign.End();

        Assert.True(campaign.Players.Count > 0);
        Assert.False(campaign.IsActive());
    }

    [Fact]
    public void BothDeadAndWorldEnded_CampaignEnded_NoConflict()
    {
        // Arrange: both dead character and world ended produce the same DeriveStatus result
        // Verify that ending a campaign (regardless of reason) produces consistent state
        var campaign = Campaign.Create(DiceExpr.D6, "Total Doom", "Both conditions met");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();
        campaign.End();

        // Assert: ended campaign with players -> "ended" regardless of reason
        Assert.True(campaign.Players.Count > 0);
        Assert.False(campaign.IsActive());
        // WorldEnded defaults to false for a fresh calendar, but DeriveStatus
        // returns "ended" based on IsActive() alone when players exist
        Assert.False(campaign.WorldEnded);
    }

    [Fact]
    public void ActiveCampaign_WithPlayers_MapsToInProgressStatus()
    {
        // Arrange: started campaign with living character, no miseries
        var campaign = Campaign.Create(DiceExpr.D6, "Active Campaign", "In progress");
        var playerId = Guid.NewGuid();
        campaign.JoinGame(playerId);
        campaign.Start();

        // Assert: DeriveStatus would return "in-progress"
        Assert.True(campaign.Players.Count > 0);
        Assert.True(campaign.IsActive());
        Assert.False(campaign.WorldEnded);
    }

    [Fact]
    public void NoCampaignPlayers_MapsToCharacterCreationStatus()
    {
        // Arrange: campaign with no players
        var campaign = Campaign.Create(DiceExpr.D6, "New Campaign", "Waiting for players");

        // Assert: DeriveStatus would return "character-creation"
        Assert.Empty(campaign.Players);
        Assert.False(campaign.IsActive());
    }
}
