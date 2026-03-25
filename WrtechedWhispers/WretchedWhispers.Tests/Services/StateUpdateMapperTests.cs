using WretchedWhispers.Api.Models;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class StateUpdateMapperTests
{
    [Fact]
    public void Map_WithNoCharacter_ReturnsNullCharacterFields()
    {
        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Test", "desc");
        var context = new SessionContext { SessionId = Guid.NewGuid() };
        context.Campaign = campaign;

        var result = StateUpdateMapper.Map(context);

        Assert.NotNull(result);
        Assert.Null(result.CharacterId);
        Assert.Null(result.CharacterName);
        Assert.Equal("charactercreation", result.Stage);
        Assert.Equal("character-creation", result.Status);
    }

    [Fact]
    public void Map_WithNoCampaign_ReturnsNullCampaignFields()
    {
        var context = new SessionContext { SessionId = Guid.NewGuid() };

        var result = StateUpdateMapper.Map(context);

        Assert.Null(result.CampaignId);
        Assert.Equal(0, result.CurrentDay);
    }
}
