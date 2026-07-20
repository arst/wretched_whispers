using Moq;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class SessionContextLoaderTests
{
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();

    private SessionContextLoader CreateLoader() =>
        new(_campaignsRepo.Object, _charactersRepo.Object, _encountersRepo.Object,
            NullLogger<SessionContextLoader>.Instance);

    [Fact]
    public async Task Load_NoCampaign_ReturnsEmptyContext()
    {
        var sessionId = Guid.NewGuid();
        _campaignsRepo.Setup(r => r.Get(sessionId)).ReturnsAsync((Campaign?)null);

        var loader = CreateLoader();
        var ctx = await loader.LoadAsync(sessionId);

        Assert.Equal(sessionId, ctx.SessionId);
        Assert.Null(ctx.Campaign);
        Assert.Null(ctx.Character);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }

    [Fact]
    public async Task Load_CampaignWithNoPlayers_ReturnsCampaignSetupNotReached()
    {
        var sessionId = Guid.NewGuid();
        var campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        typeof(Campaign).GetProperty("Id")!.SetValue(campaign, sessionId);
        _campaignsRepo.Setup(r => r.Get(sessionId)).ReturnsAsync(campaign);

        var loader = CreateLoader();
        var ctx = await loader.LoadAsync(sessionId);

        Assert.NotNull(ctx.Campaign);
        Assert.Null(ctx.CharacterId);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }
}
