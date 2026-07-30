using Moq;
using WretchedWhispers.Engine.GameTools;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;
using AbilitySet = WretchedWhispers.Core.Characters.Abilities.Abilities;

namespace WretchedWhispers.Tests.Plugins;

/// <summary>
/// Tests the merged game-tool classes (one per aggregate) that replaced the
/// Wrapper → IOperations → Adapter → Plugin stack. The behaviours pinned here are the tool-layer
/// concerns: GUID auto-fill from <see cref="SessionContext"/>, <see cref="ToolGuard"/> argument
/// validation BEFORE the domain runs, no hidden campaign-start, and combat target selection. Domain
/// rules themselves are exercised against the real Core services with mocked repositories.
/// </summary>
public class GameToolsTests
{
    private readonly SessionContext _context = new();
    private readonly Mock<ICampaignsRepository> _campaignsRepo = new();
    private readonly Mock<ICharactersRepository> _charactersRepo = new();
    private readonly Mock<IEncountersRepository> _encountersRepo = new();
    private readonly Dice _zeroDice = new(new Mock<IRandomService>().Object);

    private CampaignService MakeCampaignService(Dice? dice = null) =>
        new(_campaignsRepo.Object, _charactersRepo.Object, dice ?? _zeroDice);

    private CharacterTools CharacterTools(Dice? dice = null) => new(
        _charactersRepo.Object,
        new CharacterService(_charactersRepo.Object, dice ?? _zeroDice),
        dice ?? _zeroDice, _context);

    private CampaignTools CampaignTools() => new(
        MakeCampaignService(), _context);

    private EncounterTools EncounterTools(Dice? dice = null) => new(
        new EncounterService(dice ?? _zeroDice, _charactersRepo.Object, _encountersRepo.Object),
        _encountersRepo.Object, MakeCampaignService(dice), _context);

    // -- CharacterTools --
    // Character creation is deliberately absent from this layer: name and class are collected by the
    // create-session / successor forms and the wretch is rolled in the endpoint, so the narrator has no
    // CreateCharacter tool to call. See SessionEndpointTests for that path.

    [Fact]
    public async Task ChallengeCharacter_AutoFillsCharacterIdFromSession()
    {
        var hero = BuildHero(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools().ChallengeCharacter(12, AbilityKind.Strength);

        Assert.NotNull(result);
        // Auto-fill: the session's character id is what reached the domain.
        _charactersRepo.Verify(r => r.Get(hero.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ChallengeCharacter_Throws_WhenNoCharacter()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CharacterTools().ChallengeCharacter(12, AbilityKind.Strength));
        Assert.Contains("No character exists", ex.Message);
    }

    [Fact]
    public async Task ImproveCharacterAbility_RejectsNonPositiveDelta()
    {
        _context.SetCharacterId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CharacterTools().ImproveCharacterAbility(AbilityKind.Strength, 0));
        Assert.Contains("positive", ex.Message);
        _charactersRepo.Verify(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DegradeCharacterAbility_RejectsNonNegativeDelta()
    {
        _context.SetCharacterId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CharacterTools().DegradeCharacterAbility(AbilityKind.Strength, 1));
        Assert.Contains("negative", ex.Message);
        _charactersRepo.Verify(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyItem_RejectsNegativeSilverCost()
    {
        _context.SetCharacterId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CharacterTools().BuyItem("rusty knife", silverCost: -5));
        Assert.Contains("silverCost", ex.Message);
        _charactersRepo.Verify(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemToCharacterInventory_RejectsQuantityBelowOne()
    {
        _context.SetCharacterId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CharacterTools().AddItemToCharacterInventory("a bone", quantity: 0));
        Assert.Contains("quantity", ex.Message);
    }

    [Fact]
    public async Task ChallengeCharacter_RejectsOutOfRangeDr()
    {
        _context.SetCharacterId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => CharacterTools().ChallengeCharacter(99, AbilityKind.Agility));
        Assert.Contains("between 2 and 20", ex.Message);
    }

    // -- CampaignTools --

    [Fact]
    public async Task ConfigureCampaign_UpdatesExistingCampaign()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Default", "default desc");
        _context.SetCampaignId(campaign.Id);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        var result = await CampaignTools().ConfigureCampaign("Doom Awaits", "A slow march to annihilation");

        Assert.Equal("Doom Awaits", result.Name);
        Assert.Equal("A slow march to annihilation", result.Description);
        _campaignsRepo.Verify(r => r.SaveCampaign(campaign), Times.Once);
    }

    [Fact]
    public async Task ConfigureCampaign_AutoStartsWhenPlayerAlreadyJoined()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Dark", "desc");
        campaign.JoinGame(Guid.NewGuid()); // a player has already joined before setup completes
        _context.SetCampaignId(campaign.Id);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        await CampaignTools().ConfigureCampaign("Doom Awaits", "A slow march to annihilation");

        Assert.True(campaign.IsActive());
        _campaignsRepo.Verify(r => r.SaveCampaign(campaign), Times.Once);
    }

    [Fact]
    public async Task AdvanceTime_RejectsNonPositiveHours()
    {
        _context.SetCampaignId(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => CampaignTools().AdvanceTime(0));
        Assert.Contains("hours", ex.Message);
    }

    // -- EncounterTools --

    [Fact]
    public async Task CreateEncounter_SetsActiveEncounterId()
    {
        var result = await EncounterTools().CreateEncounter("Goblins", "A goblin ambush", "Hostile");

        Assert.Equal(result.Id, _context.ActiveEncounterId);
    }

    [Fact]
    public async Task CreateEncounter_Unknown_ReturnsRolledReactionInDto()
    {
        // 0-based mock: 3,2 -> 2d6 = 7 -> Indifferent -> Friendly.
        var mock = new Mock<IRandomService>();
        var queue = new Queue<int>([3, 2]);
        mock.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(() => queue.Dequeue());
        var dice = new Dice(mock.Object);

        var result = await EncounterTools(dice).CreateEncounter("Strangers", "Figures in the fog", "Unknown");

        Assert.Equal("Friendly", result.Disposition);
        Assert.Equal("Indifferent", result.Reaction);
        Assert.Equal(7, result.ReactionRoll);
    }

    [Fact]
    public async Task CreateEncounter_Hostile_ReportsHostileDispositionWithoutReaction()
    {
        var result = await EncounterTools().CreateEncounter("Ambush", "Bandits leap out", "Hostile");

        Assert.Equal("Hostile", result.Disposition);
        Assert.Null(result.Reaction);
        Assert.Null(result.ReactionRoll);
    }

    [Fact]
    public async Task CreateEncounter_NumericType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EncounterTools().CreateEncounter("Strangers", "Figures in the fog", "5"));
    }

    [Fact]
    public async Task TurnEncounterHostile_FlipsDisposition()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, _zeroDice);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);
        _context.SetActiveEncounterId(encounter.Id);

        var result = await EncounterTools().TurnEncounterHostile();

        Assert.Equal("Hostile", result.Disposition);
        _encountersRepo.Verify(r => r.Save(It.Is<Encounter>(e => e.CurrentType == EncounterType.Hostile)), Times.Once);
    }

    [Fact]
    public async Task TurnEncounterHostile_WithoutActiveEncounter_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => EncounterTools().TurnEncounterHostile());
    }

    [Fact]
    public async Task ResolveCombatRound_Attack_SelectsTheNamedAdversary()
    {
        var hero = BuildHero(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        // Two living adversaries; the named one is second, so a match-by-name (not "first") is proven
        // by which one takes damage. Scripted dice force a hit so the damage is observable.
        var encounter = Encounter.Create("Ambush", "test", EncounterType.Hostile, _zeroDice);
        var bystander = NewAdversary("Snivelling Cur");
        var target = NewAdversary("Ragged Bandit");
        encounter.AddAdversary(bystander);
        encounter.AddAdversary(target);
        encounter.StartEncounter();
        _context.ActiveEncounter = encounter;
        _context.SetActiveEncounterId(encounter.Id);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);

        // d20 -> 15 (hit, no crit), d6 -> 4 (sword damage).
        var hitDice = ScriptedDice(14, 3);
        var tools = new EncounterTools(
            new EncounterService(hitDice, _charactersRepo.Object, _encountersRepo.Object),
            _encountersRepo.Object, MakeCampaignService(hitDice), _context);

        var result = await tools.ResolveCombatRound("Attack", "Ragged Bandit");

        Assert.NotNull(result.PlayerAttack);
        Assert.True(result.PlayerAttack.Hit);
        Assert.Equal("Ragged Bandit", result.PlayerAttack.Target);
        Assert.True(target.Hp.Current < target.Hp.Max, "the named adversary should have taken damage");
        Assert.Equal(bystander.Hp.Max, bystander.Hp.Current); // the other adversary is untouched
    }

    [Fact]
    public async Task ResolveCombatRound_Other_ResolvesRetaliationFromLivingAdversary()
    {
        var hero = BuildHero(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var encounter = Encounter.Create("Ambush", "test", EncounterType.Hostile, _zeroDice);
        encounter.AddAdversary(NewAdversary("Thug"));
        encounter.StartEncounter();
        _context.ActiveEncounter = encounter;
        _context.SetActiveEncounterId(encounter.Id);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);

        var result = await EncounterTools().ResolveCombatRound("Other");

        Assert.Null(result.PlayerAttack);
        Assert.Single(result.Retaliations);
        Assert.Equal("Thug", result.Retaliations[0].AdversaryName);
    }

    [Fact]
    public async Task CompleteResolution_ResolvesEncounterAndClearsContext()
    {
        var encId = Guid.NewGuid();
        _context.SetActiveEncounterId(encId);
        var encounter = CreateEndedEncounter(encId);
        _encountersRepo.Setup(r => r.Get(encId)).ReturnsAsync(encounter);

        await EncounterTools().CompleteResolution();

        Assert.Null(_context.ActiveEncounterId);
        Assert.True(encounter.IsResolved);
        _encountersRepo.Verify(r => r.Save(encounter), Times.Once);
    }

    [Fact]
    public async Task CompleteResolution_Throws_WhenNoActiveEncounter()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EncounterTools().CompleteResolution());
        Assert.Contains("No encounter to resolve", ex.Message);
    }

    // -- DiceTools --

    [Fact]
    public void Roll_ReturnsFormulaAndResult()
    {
        var result = new DiceTools(_zeroDice).Roll("1d6");

        Assert.Equal("1d6", result.Formula);
        Assert.InRange(result.Result, 1, 6);
    }

    [Fact]
    public void Roll_RejectsMalformedDiceExpression()
    {
        var ex = Assert.Throws<ArgumentException>(() => new DiceTools(_zeroDice).Roll("not-a-die"));
        Assert.Contains("d6", ex.Message);
    }

    // -- helpers --

    private static Character BuildHero(Dice dice)
    {
        // Abilities ctor order is (agility, presence, strength, toughness). Sword is melee => Strength.
        var abilities = new AbilitySet(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(0),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 10, FoodDays: 3, Container: "backpack (7 items)",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Sword),
            Armor: new Armor(ArmorTier.None),
            Shield: null, Scrolls: []);
        return Character.Create(Guid.NewGuid(), "Hero", 20, abilities, equipment, dice);
    }

    private static Adversary NewAdversary(string name) =>
        new(name, new HitPoints(6, 6), new Armor(ArmorTier.None), 7,
            new AttackProfile("Knife", DiceExpr.Parse("1d4")));

    private static Dice ScriptedDice(params int[] zeroBasedRolls)
    {
        var queue = new Queue<int>(zeroBasedRolls);
        var random = new Mock<IRandomService>();
        random.Setup(r => r.GenerateRandomRoll(It.IsAny<int>()))
            .Returns(() => queue.Count > 0 ? queue.Dequeue() : 0);
        return new Dice(random.Object);
    }

    private static Encounter CreateEndedEncounter(Guid id)
    {
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
}
