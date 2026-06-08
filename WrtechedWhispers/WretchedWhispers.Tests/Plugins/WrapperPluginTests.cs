using Moq;
using WretchedWhispers.Api.Plugins.GameMasterPlugins;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
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
    public async Task CreateCharacter_DelegatesToInner_SetsCharacterId_JoinsButDoesNotStartCampaign()
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

        // Stage/state integrity (Phase 2c): creating a character links it to the campaign but must
        // NOT start the campaign — starting is an explicit CampaignSetup step. So the derived stage
        // stays CampaignSetup, not Exploration.
        Assert.False(campaign.IsActive());
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

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);
        var result = await wrapper.CreateEncounter("Goblins", "A goblin ambush", "Hostile");

        Assert.Equal(encId, result.Id);
        Assert.Equal(encId, _context.ActiveEncounterId);
    }

    [Fact]
    public async Task AttackPlayer_AutoSelectsFirstLivingAdversary()
    {
        var charId = Guid.NewGuid();
        _context.SetCharacterId(charId);
        var (encounter, advId) = CreateStartedEncounterWithAdversary("Thug");
        _context.ActiveEncounter = encounter;
        _context.SetActiveEncounterId(encounter.Id);

        var inner = new Mock<IEncounterOperations>();
        inner.Setup(p => p.AttackPlayer(encounter.Id, advId, charId))
            .ReturnsAsync(new AdversaryAttackOutcomeDto(3));

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);
        var result = await wrapper.AttackPlayer();

        Assert.Equal(3, result.DamageDealt);
        inner.Verify(p => p.AttackPlayer(encounter.Id, advId, charId), Times.Once);
    }

    [Fact]
    public async Task AttackAdversary_MatchesByName()
    {
        var charId = Guid.NewGuid();
        _context.SetCharacterId(charId);
        var (encounter, advId) = CreateStartedEncounterWithAdversary("Ragged Bandit");
        _context.ActiveEncounter = encounter;
        _context.SetActiveEncounterId(encounter.Id);

        var inner = new Mock<IEncounterOperations>();
        inner.Setup(p => p.AttackAdversary(encounter.Id, charId, advId))
            .ReturnsAsync(new CharacterAttackOutcomeDto(true, 5, false, false, false, false));

        var wrapper = new EncounterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);
        var result = await wrapper.AttackAdversary("Ragged Bandit");

        Assert.True(result.IsHit);
        inner.Verify(p => p.AttackAdversary(encounter.Id, charId, advId), Times.Once);
    }

    private static (Encounter encounter, Guid adversaryId) CreateStartedEncounterWithAdversary(string adversaryName)
    {
        var dice = new Dice(new Mock<IRandomService>().Object);
        var encounter = Encounter.Create("Test Encounter", "test", EncounterType.Hostile, dice);
        var hp = new HitPoints(4, 4);
        var armor = new Armor(NoArmorTier.Instance);
        var attack = new AttackProfile("Knife", DiceExpr.Parse("1d4"));
        var adversary = new Adversary(adversaryName, hp, armor, 7, attack);
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        return (encounter, adversary.Id);
    }

    // -- ResolutionWrapperPlugin --

    [Fact]
    public async Task CompleteResolution_ResolvesEncounterAndClearsContext()
    {
        var encId = Guid.NewGuid();
        _context.SetActiveEncounterId(encId);

        // Create an encounter in ended-but-unresolved state via JSON constructor
        var encounter = CreateEndedEncounter(encId);

        var encRepo = new Mock<IEncountersRepository>();
        encRepo.Setup(r => r.Get(encId)).ReturnsAsync(encounter);

        var wrapper = new ResolutionWrapperPlugin(_context, encRepo.Object);
        await wrapper.CompleteResolution();

        Assert.Null(_context.ActiveEncounterId);
        Assert.True(encounter.IsResolved);
        encRepo.Verify(r => r.Save(encounter), Times.Once);
    }

    private static Encounter CreateEndedEncounter(Guid id)
    {
        // Use JSON round-trip to create encounter in desired state
        var json = $$"""
        {
            "Id": "{{id}}",
            "InitialType": 0,
            "Name": "Test",
            "Description": "test",
            "Adversaries": [],
            "IsStarted": true,
            "IsEnded": true,
            "IsResolved": false
        }
        """;
        return System.Text.Json.JsonSerializer.Deserialize<Encounter>(json)!;
    }

    [Fact]
    public async Task CompleteResolution_Throws_WhenNoActiveEncounter()
    {
        var encRepo = new Mock<IEncountersRepository>();
        var wrapper = new ResolutionWrapperPlugin(_context, encRepo.Object);

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

    // -- Argument validation (#4): bad model args are rejected with a clear message BEFORE hitting
    //    the domain, so Agent Framework feeds the reason back and the model can self-correct. --

    [Fact]
    public async Task ImproveCharacterAbility_RejectsNonPositiveDelta()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.ImproveCharacterAbility(AbilityKind.Strength, 0));
        Assert.Contains("positive", ex.Message);
        inner.Verify(p => p.ImproveCharacterAbility(It.IsAny<Guid>(), It.IsAny<AbilityKind>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DegradeCharacterAbility_RejectsNonNegativeDelta()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.DegradeCharacterAbility(AbilityKind.Strength, 1));
        Assert.Contains("negative", ex.Message);
        inner.Verify(p => p.DegradeCharacterAbility(It.IsAny<Guid>(), It.IsAny<AbilityKind>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BuyItem_RejectsNegativeSilverCost()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.BuyItem("rusty knife", silverCost: -5));
        Assert.Contains("silverCost", ex.Message);
        inner.Verify(p => p.BuyItem(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddItemToCharacterInventory_RejectsQuantityBelowOne()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.AddItemToCharacterInventory("a bone", quantity: 0));
        Assert.Contains("quantity", ex.Message);
    }

    [Fact]
    public async Task ChallengeCharacter_RejectsOutOfRangeDr()
    {
        _context.SetCharacterId(Guid.NewGuid());
        var inner = new Mock<ICharacterOperations>();
        var wrapper = new CharacterWrapperPlugin(inner.Object, _context, _campaignsRepo.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => wrapper.ChallengeCharacter(99, AbilityKind.Agility));
        Assert.Contains("between 2 and 20", ex.Message);
    }

    [Fact]
    public async Task AdvanceTime_RejectsNonPositiveHours()
    {
        _context.SetCampaignId(Guid.NewGuid());
        var inner = new Mock<ICampaignOperations>();
        var wrapper = new CampaignWrapperPlugin(inner.Object, _campaignsRepo.Object, _context);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => wrapper.AdvanceTime(0));
        Assert.Contains("hours", ex.Message);
        inner.Verify(p => p.AdvanceTime(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Roll_RejectsMalformedDiceExpression()
    {
        var inner = new Mock<IDiceOperations>();
        var wrapper = new DiceWrapperPlugin(inner.Object);

        var ex = Assert.Throws<ArgumentException>(() => wrapper.Roll("not-a-die"));
        Assert.Contains("d6", ex.Message);
        inner.Verify(p => p.Roll(It.IsAny<string>()), Times.Never);
    }
}
