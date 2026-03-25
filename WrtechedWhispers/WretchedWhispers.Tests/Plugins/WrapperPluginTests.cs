using Moq;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Semantic;
using WretchedWhispers.Semantic.Models;
using Xunit;

namespace WretchedWhispers.Tests.Plugins;

public class WrapperPluginTests
{
    private readonly SessionContext _context = new();
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();

    // -- CharacterWrapperPlugin --

    [Fact]
    public async Task CreateCharacter_DelegatesToInner_SetsCharacterId_AndJoinsCampaign()
    {
        var charId = Guid.NewGuid();
        var campId = Guid.NewGuid();
        _context.SetCampaignId(campId);

        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Test", "desc");
        // Use reflection to set the campaign's Id to match our expected campId
        typeof(Campaign).GetProperty("Id")!.SetValue(campaign, campId);
        _campaignsRepo.Setup(r => r.Get(campId)).ReturnsAsync(campaign);

        var inner = new Mock<ICharacterOperations>();
        inner.Setup(p => p.CreateCharacter("Gruk"))
            .ReturnsAsync(new CharacterDto { Id = charId, Name = "Gruk", Inventory = new InventoryDto() });

        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);
        var result = await wrapper.CreateCharacter("Gruk");

        Assert.Equal(charId, result.Id);
        Assert.Equal(charId, _context.CharacterId);
        Assert.Contains(charId, campaign.Players);
        _campaignsRepo.Verify(r => r.SaveCampaign(campaign), Times.Once);
    }

    [Fact]
    public async Task CreateCharacter_Throws_WhenCharacterAlreadyExists()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.CreateCharacter("Gruk"));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task ChallengeCharacter_AutoFillsCharacterId()
    {
        var charId = Guid.NewGuid();
        _context.SetCharacterId(charId);
        var inner = new Mock<ICharacterOperations>();
        inner.Setup(p => p.ChallengeCharacter(charId, 12, AbilityKind.Strength))
            .ReturnsAsync(new ChallengeOutcomeDto(true));

        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);
        var result = await wrapper.ChallengeCharacter(12, AbilityKind.Strength);

        Assert.True(result.IsSuccess);
        inner.Verify(p => p.ChallengeCharacter(charId, 12, AbilityKind.Strength), Times.Once);
    }

    [Fact]
    public async Task ChallengeCharacter_Throws_WhenNoCharacter()
    {
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.ChallengeCharacter(12, AbilityKind.Strength));
        Assert.Contains("CreateCharacter", ex.Message);
    }

    // -- CampaignWrapperPlugin --

    [Fact]
    public async Task ConfigureCampaign_UpdatesExistingCampaign()
    {
        var campId = Guid.NewGuid();
        _context.SetCampaignId(campId);

        var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Default", "default desc");
        typeof(Campaign).GetProperty("Id")!.SetValue(campaign, campId);
        _campaignsRepo.Setup(r => r.Get(campId)).ReturnsAsync(campaign);

        var inner = new Mock<ICampaignOperations>();
        var wrapper = new CampaignWrapperPlugin(inner.Object, _campaignsRepo.Object, _context);
        var result = await wrapper.ConfigureCampaign("d100", "Doom Awaits", "A slow march to annihilation");

        Assert.Equal("Doom Awaits", result.Name);
        Assert.Equal("A slow march to annihilation", result.Description);
        _campaignsRepo.Verify(r => r.SaveCampaign(campaign), Times.Once);
    }

    [Fact]
    public async Task StartCampaign_AutoFillsCampaignId()
    {
        var campId = Guid.NewGuid();
        _context.SetCampaignId(campId);
        var inner = new Mock<ICampaignOperations>();
        inner.Setup(p => p.StartCampaign(campId))
            .ReturnsAsync(new CampaignDto(campId, "Dark", "desc", 1, 0, []));

        var wrapper = new CampaignWrapperPlugin(inner.Object, _campaignsRepo.Object, _context);
        await wrapper.StartCampaign();

        inner.Verify(p => p.StartCampaign(campId), Times.Once);
    }

    [Fact]
    public async Task AdvanceTime_AutoFillsCampaignId()
    {
        var campId = Guid.NewGuid();
        _context.SetCampaignId(campId);
        var inner = new Mock<ICampaignOperations>();
        inner.Setup(p => p.AdvanceTime(campId, 6))
            .ReturnsAsync(new AdvanceTimeOutcomeDto([], false, false));

        var wrapper = new CampaignWrapperPlugin(inner.Object, _campaignsRepo.Object, _context);
        await wrapper.AdvanceTime(6);

        inner.Verify(p => p.AdvanceTime(campId, 6), Times.Once);
    }

    // -- EncounterWrapperPlugin --

    [Fact]
    public async Task CreateEncounter_DelegatesToInner_AndSetsActiveEncounterId()
    {
        var encId = Guid.NewGuid();
        var inner = new Mock<IEncounterOperations>();
        inner.Setup(p => p.CreateEncounter("Goblins", "A goblin ambush", "Hostile"))
            .ReturnsAsync(new EncounterDto { Id = encId, Name = "Goblins" });

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context);
        var result = await wrapper.CreateEncounter("Goblins", "A goblin ambush", "Hostile");

        Assert.Equal(encId, result.Id);
        Assert.Equal(encId, _context.ActiveEncounterId);
    }

    [Fact]
    public async Task AttackPlayer_AutoFillsEncounterIdAndPlayerId()
    {
        var encId = Guid.NewGuid();
        var charId = Guid.NewGuid();
        var advId = Guid.NewGuid();
        _context.SetActiveEncounterId(encId);
        _context.SetCharacterId(charId);
        var inner = new Mock<IEncounterOperations>();
        inner.Setup(p => p.AttackPlayer(encId, advId, charId))
            .ReturnsAsync(new AdversaryAttackOutcomeDto(3));

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context);
        var result = await wrapper.AttackPlayer(advId);

        Assert.Equal(3, result.DamageDealt);
        inner.Verify(p => p.AttackPlayer(encId, advId, charId), Times.Once);
    }

    [Fact]
    public async Task AttackAdversary_AutoFillsEncounterIdAndPlayerId()
    {
        var encId = Guid.NewGuid();
        var charId = Guid.NewGuid();
        var advId = Guid.NewGuid();
        _context.SetActiveEncounterId(encId);
        _context.SetCharacterId(charId);
        var inner = new Mock<IEncounterOperations>();
        inner.Setup(p => p.AttackAdversary(encId, charId, advId))
            .ReturnsAsync(new CharacterAttackOutcomeDto(true, 5, false, false, false, false));

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context);
        var result = await wrapper.AttackAdversary(advId);

        Assert.True(result.IsHit);
        inner.Verify(p => p.AttackAdversary(encId, charId, advId), Times.Once);
    }

    // -- ResolutionWrapperPlugin --

    [Fact]
    public async Task CompleteResolution_ClearsActiveEncounter()
    {
        var encId = Guid.NewGuid();
        _context.SetActiveEncounterId(encId);

        var wrapper = new ResolutionWrapperPlugin(_context);
        await wrapper.CompleteResolution();

        Assert.Null(_context.ActiveEncounterId);
    }

    [Fact]
    public async Task CompleteResolution_Throws_WhenNoActiveEncounter()
    {
        var wrapper = new ResolutionWrapperPlugin(_context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.CompleteResolution());
        Assert.Contains("No encounter to resolve", ex.Message);
    }

    // -- DiceWrapperPlugin --

    [Fact]
    public void Roll_DelegatesToInner()
    {
        var inner = new Mock<IDiceOperations>();
        inner.Setup(p => p.Roll("1d6")).Returns(new DiceRollResult("1d6", 4));

        var wrapper = new DiceWrapperPlugin(inner.Object);
        var result = wrapper.Roll("1d6");

        Assert.Equal(4, result.Result);
        Assert.Equal("1d6", result.Formula);
        inner.Verify(p => p.Roll("1d6"), Times.Once);
    }
}
