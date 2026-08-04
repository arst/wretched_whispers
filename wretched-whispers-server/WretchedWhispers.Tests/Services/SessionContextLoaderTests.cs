using Moq;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
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
    private readonly Dice _dice = new(new Mock<IRandomService>().Object);

    private SessionContextLoader CreateLoader() =>
        new(_campaignsRepo.Object, _charactersRepo.Object, _encountersRepo.Object,
            NullLogger<SessionContextLoader>.Instance);

    [Fact]
    public async Task Load_NoCampaign_ReturnsEmptyContext()
    {
        var sessionId = Guid.NewGuid();
        _campaignsRepo.Setup(r => r.Get(sessionId)).ReturnsAsync((Campaign?)null);

        var ctx = await CreateLoader().LoadAsync(sessionId);

        Assert.Equal(sessionId, ctx.SessionId);
        Assert.Null(ctx.Campaign);
        Assert.Null(ctx.Character);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }

    [Fact]
    public async Task Load_CampaignWithNoPlayers_DerivesCharacterCreationStage()
    {
        // The session id IS the campaign id: the loader looks the campaign up by it.
        var campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        var ctx = await CreateLoader().LoadAsync(campaign.Id);

        Assert.NotNull(ctx.Campaign);
        Assert.Null(ctx.CharacterId);
        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }

    [Fact]
    public async Task Load_CampaignWithCharacterAndActiveEncounter_PopulatesFullContext()
    {
        var character = TestCharacters.Create(_dice);
        var campaign = Campaign.Create(Difficulty.Grim, "Test", "desc");
        campaign.JoinGame(character.Id);
        campaign.Start();

        var encounter = Encounter.Create("Ambush", "test", EncounterType.Hostile, _dice);
        encounter.AddAdversary(new Adversary(
            "Thug", new HitPoints(6, 6), new Armor(ArmorTier.None), 7,
            new AttackProfile("Knife", DiceExpr.Parse("1d4"))));
        encounter.StartEncounter();
        campaign.AddEncounter(encounter.Id);

        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        _charactersRepo.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);

        var ctx = await CreateLoader().LoadAsync(campaign.Id);

        Assert.Same(character, ctx.Character);
        Assert.Equal(character.Id, ctx.CharacterId);
        Assert.Same(encounter, ctx.ActiveEncounter);
        Assert.Equal(encounter.Id, ctx.ActiveEncounterId);
        Assert.Equal(SessionStage.Combat, ctx.DeriveStage());
    }
}
