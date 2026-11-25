using System.Text.Json;
using Microsoft.Extensions.AI;
using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools;
using WretchedWhispers.Engine.GameTools.Models;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using Xunit;

namespace WretchedWhispers.Tests.GameTools;

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
        new EncounterService(dice ?? _zeroDice, _charactersRepo.Object, _encountersRepo.Object),
        MakeCampaignService(dice), _context);

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

        await CharacterTools().ChallengeCharacter(12, AbilityKind.Strength, hoursTaken: 0);

        // Auto-fill: the session's character id is what reached the domain.
        _charactersRepo.Verify(r => r.Get(hero.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ChallengeCharacter_Throws_WhenNoCharacter()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CharacterTools().ChallengeCharacter(12, AbilityKind.Strength, hoursTaken: 0));
        Assert.Contains("No character exists", ex.Message);
    }

    [Theory]
    [InlineData("ImproveAbility_ZeroDelta")]
    [InlineData("DegradeAbility_PositiveDelta")]
    [InlineData("ImproveAbility_AboveTwo")]
    [InlineData("DegradeAbility_BelowMinusTwo")]
    [InlineData("BuyItem_NegativeSilverCost")]
    [InlineData("AddItem_QuantityBelowOne")]
    [InlineData("Challenge_OutOfRangeDr")]
    [InlineData("SellItem_NegativePrice")]
    [InlineData("SellItem_QuantityBelowOne")]
    [InlineData("EquipWeapon_NegativeCost")]
    [InlineData("EquipWeapon_UndefinedKind")]
    [InlineData("EquipArmor_NegativeCost")]
    [InlineData("EquipShield_NegativeCost")]
    public async Task CharacterToolGuards_RejectBadArguments_BeforeTouchingTheRepository(string call)
    {
        _context.SetCharacterId(Guid.NewGuid());
        var tools = CharacterTools();

        Func<Task> act = call switch
        {
            "ImproveAbility_ZeroDelta" => () => tools.ImproveCharacterAbility(AbilityKind.Strength, 0),
            "DegradeAbility_PositiveDelta" => () => tools.DegradeCharacterAbility(AbilityKind.Strength, 1),
            "ImproveAbility_AboveTwo" => () => tools.ImproveCharacterAbility(AbilityKind.Strength, 3),
            "DegradeAbility_BelowMinusTwo" => () => tools.DegradeCharacterAbility(AbilityKind.Strength, -3),
            "BuyItem_NegativeSilverCost" => () => tools.BuyItem("rusty knife", silverCost: -5),
            "AddItem_QuantityBelowOne" => () => tools.AddItemToCharacterInventory("a bone", quantity: 0),
            "Challenge_OutOfRangeDr" => () => tools.ChallengeCharacter(99, AbilityKind.Agility, hoursTaken: 0),
            "SellItem_NegativePrice" => () => tools.SellItem("a bone", silverPrice: -1),
            "SellItem_QuantityBelowOne" => () => tools.SellItem("a bone", silverPrice: 1, quantity: 0),
            "EquipWeapon_NegativeCost" => () => tools.EquipWeapon(WeaponKind.Sword, silverCost: -1),
            "EquipWeapon_UndefinedKind" => () => tools.EquipWeapon((WeaponKind)99),
            "EquipArmor_NegativeCost" => () => tools.EquipArmor(ArmorTierDto.Light, silverCost: -1),
            "EquipShield_NegativeCost" => () => tools.EquipShield(silverCost: -1),
            _ => throw new ArgumentOutOfRangeException(nameof(call))
        };

        await Assert.ThrowsAsync<ArgumentException>(act);
        _charactersRepo.Verify(r => r.Get(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private Character HeroInSession(Dice? dice = null)
    {
        var hero = BuildHero(dice ?? _zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);
        return hero;
    }

    private static InventoryItem Carry(Character hero, string description, int quantity = 1)
    {
        var item = new InventoryItem(Guid.NewGuid(), description, false, false, quantity);
        hero.AddItem(item);
        return item;
    }

    // Game State lists an item as "life elixir x2", and the model copies the quantity into the name.
    [Fact]
    public async Task UseItem_AcceptsTheNameWithItsQuantitySuffix()
    {
        var hero = HeroInSession();
        var elixir = Carry(hero, "life elixir", quantity: 2);

        await CharacterTools().UseItemFromCharacterInventory("life elixir x2");

        Assert.Equal(1, elixir.Quantity);
    }

    [Fact]
    public async Task UseItem_PrefersAnExactNameOverAStrippedOne()
    {
        var hero = HeroInSession();
        var bolts = Carry(hero, "bolts", quantity: 5);
        var bundle = Carry(hero, "bolts x20", quantity: 2);

        await CharacterTools().UseItemFromCharacterInventory("bolts x20");

        Assert.Equal(1, bundle.Quantity);
        Assert.Equal(5, bolts.Quantity);
    }

    [Fact]
    public async Task UseItem_HealingItem_ReportsWhatItHealed()
    {
        // d4 daily powers; a wound of 6; d6 = 3 healed.
        var dice = ScriptedDice(0, 5, 2);
        var hero = HeroInSession(dice);
        hero.SufferConsequence(ChallengeConsequence.Deadly, DifficultyPresets.For(Difficulty.Hardcore), dice);
        var woundedAt = hero.Hp.Current;
        hero.AddItem(new InventoryItem(Guid.NewGuid(), "life elixir", false, true, 1, ItemEffect.Heal));

        var used = await CharacterTools(dice).UseItemFromCharacterInventory("life elixir");

        Assert.Equal(3, used.HpHealed);
        Assert.Equal(woundedAt + 3, used.Character.CurrentHp);
        Assert.Empty(used.Character.Inventory.Items);
    }

    [Theory]
    [InlineData(true, ItemEffect.Heal)]
    [InlineData(false, ItemEffect.None)]
    public async Task BoughtAndFoundItems_CarryTheHealingTheNarratorDeclared(bool heals, ItemEffect expected)
    {
        var hero = HeroInSession();

        await CharacterTools().BuyItem("bitter draught", silverCost: 5, heals: heals);
        await CharacterTools().AddItemToCharacterInventory("grave moss", heals: heals);

        Assert.Equal(2, hero.Inventory.InventoryItems.Count);
        Assert.All(hero.Inventory.InventoryItems, item => Assert.Equal(expected, item.Effect));
    }

    [Fact]
    public async Task EquipTools_ChangeTheSheetAndChargeTheSilver()
    {
        var hero = HeroInSession();

        await CharacterTools().EquipWeapon(WeaponKind.Flail, silverCost: 4);
        await CharacterTools().EquipArmor(ArmorTierDto.Medium);
        var sheet = await CharacterTools().EquipShield(silverCost: 2);

        Assert.Equal(WeaponKind.Flail, sheet.WeaponKind);
        Assert.Equal(ArmorTierDto.Medium, sheet.ArmorTier);
        Assert.True(sheet.HasShield);
        Assert.Equal(4, sheet.Silver);
        Assert.Equal(WeaponKind.Flail, hero.Weapon.Kind);
        _charactersRepo.Verify(r => r.Save(hero, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task EquipWeapon_RefusesNaturalAttacks()
    {
        var hero = HeroInSession();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CharacterTools().EquipWeapon(WeaponKind.Fangs));

        Assert.Equal(WeaponKind.Sword, hero.Weapon.Kind);
    }

    [Fact]
    public async Task SellItem_ResolvesTheItemByDescription()
    {
        var hero = HeroInSession();
        var torches = Carry(hero, "torches", quantity: 3);

        var sheet = await CharacterTools().SellItem("Torches x3", silverPrice: 9, quantity: 3);

        Assert.DoesNotContain(torches, hero.Inventory.InventoryItems);
        Assert.Equal(19, sheet.Silver);
        _charactersRepo.Verify(r => r.Save(hero, It.IsAny<CancellationToken>()), Times.Once);
    }

    // The wielded weapon is on the sheet, not in the pack: "sell my sword" found nothing to sell.
    [Fact]
    public async Task SellItem_NamingTheWieldedWeapon_PaysAndDisarms()
    {
        var hero = HeroInSession();

        var sheet = await CharacterTools().SellItem("Sword", silverPrice: 18);

        Assert.Equal(WeaponKind.Improvised, sheet.WeaponKind);
        Assert.Equal(28, sheet.Silver);
        _charactersRepo.Verify(r => r.Save(hero, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SellItem_PrefersACarriedItemOverTheWieldedWeapon()
    {
        var hero = HeroInSession();
        Carry(hero, "sword");

        var sheet = await CharacterTools().SellItem("Sword", silverPrice: 18);

        Assert.Equal(WeaponKind.Sword, sheet.WeaponKind);
        Assert.Empty(hero.Inventory.InventoryItems);
    }

    // The model copied the scroll's whole line from Game State as its name.
    [Theory]
    [InlineData("Scroll: Hermetic Step (Sacred): For 2d10 minutes the caster finds every trap in their path.")]
    [InlineData("Scroll: Hermetic Step (Sacred)")]
    [InlineData("hermetic step")]
    public async Task CastScroll_FindsTheScrollFromItsGameStateLine(string asTheModelWroteIt)
    {
        // d4 daily powers; d20 = 15 to cast.
        var dice = ScriptedDice(0, 14);
        var hero = TestCharacters.Create(dice, scrolls:
        [
            new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech"),
            new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Hermetic Step")
        ]);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var cast = await CharacterTools(dice).CastScroll(asTheModelWroteIt);

        Assert.Equal("Hermetic Step", cast.Scroll);
    }

    [Fact]
    public void CombatRoundOutcome_ThatKilledThePlayer_SaysDead()
    {
        var round = new CombatRoundOutcome(null, null, null, [], [], true, EncounterEndReason.PlayerDead, 0, false);

        Assert.True(CombatRoundOutcomeDto.From(round).PlayerDead);
        Assert.False(CombatRoundOutcomeDto.From(round with { EndReason = EncounterEndReason.None }).PlayerDead);
    }

    [Fact]
    public async Task GainSilver_PaysWhatTheDiceRollAndSaysHowMuch()
    {
        var hero = HeroInSession();

        var found = await CharacterTools().GainSilver(SilverFind.Purse, "the toll-keeper's strongbox");

        // Zero dice: each of the purse's 2d10 shows 1.
        Assert.Equal(2, found.SilverFound);
        Assert.Equal(12, found.SilverNow);
        Assert.Equal(12, hero.Silver);
        _charactersRepo.Verify(r => r.Save(hero, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CastScroll_TellsWhatTheScrollDidAndWhoItHit()
    {
        // d4 daily powers; d20 = 15 to cast; d2 = 1 creature; d8 = 8.
        var dice = ScriptedDice(0, 14, 0, 7);
        var hero = TestCharacters.Create(dice,
            scrolls: new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, "Palms Open the Southern Gate"));
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);
        var encounter = Encounter.Create("Ambush", "test", EncounterType.Hostile, _zeroDice);
        encounter.AddAdversary(NewAdversary("Goblin"));
        encounter.StartEncounter();
        _context.SetActiveEncounterId(encounter.Id);
        _encountersRepo.Setup(r => r.Get(encounter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(encounter);

        var cast = await CharacterTools(dice).CastScroll("palms open the southern gate");

        Assert.True(cast.Succeeded);
        Assert.Equal("Palms Open the Southern Gate", cast.Scroll);
        Assert.False(string.IsNullOrWhiteSpace(cast.Effect));
        var hit = Assert.Single(cast.AdversariesHit);
        Assert.Equal(("Goblin", 8, true), (hit.Adversary, hit.Damage, hit.Killed));
        Assert.Equal(hero.Hp.Current, cast.CurrentHp);
    }

    // The real repository deserializes a fresh character on every Get, so a tool that reports from the
    // instance it loaded, after a service changed another one, reports stale numbers.
    [Fact]
    public async Task CastScroll_ReportsTheCastersHpAsSaved_NotAsFirstLoaded()
    {
        // d4 daily powers; a wound of 6; d20 = 15 to cast; d10 = 4 healed.
        var dice = ScriptedDice(0, 5, 14, 3);
        var hero = TestCharacters.Create(dice,
            scrolls: new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Grace of a Dead Saint"));
        hero.SufferConsequence(ChallengeConsequence.Deadly, DifficultyPresets.For(Difficulty.Hardcore), dice);
        var woundedAt = hero.Hp.Current;
        var json = AggregateJsonOptions.Create();
        var stored = JsonSerializer.Serialize(hero, json);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => JsonSerializer.Deserialize<Character>(stored, json));
        _charactersRepo.Setup(r => r.Save(It.IsAny<Character>(), It.IsAny<CancellationToken>()))
            .Callback((Character saved, CancellationToken _) => stored = JsonSerializer.Serialize(saved, json));

        var cast = await CharacterTools(dice).CastScroll("Grace of a Dead Saint");

        Assert.Equal(4, cast.HpHealed);
        Assert.Equal(woundedAt + 4, cast.CurrentHp);
    }

    /// <summary>Every d4 rolls 3 (daily powers, damage, the Broken table: survives), every d6 rolls 6 (a
    /// lost eye) and every d20 rolls 5 (a failed test, dodge or casting).</summary>
    private static Dice DiceThatBreakWithALostEye()
    {
        var random = new Mock<IRandomService>();
        random.Setup(r => r.GenerateRandomRoll(4)).Returns(2);
        random.Setup(r => r.GenerateRandomRoll(6)).Returns(5);
        random.Setup(r => r.GenerateRandomRoll(20)).Returns(4);
        return new Dice(random.Object);
    }

    [Fact]
    public async Task ChallengeCharacter_NamesTheInjuryABreakGave()
    {
        var dice = DiceThatBreakWithALostEye();
        var hero = TestCharacters.Create(dice, maxHp: 1);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools(dice).ChallengeCharacter(
            12, AbilityKind.Strength, 0, ChallengeConsequence.Serious);

        Assert.Equal("lost eye", result.NewInjury);
        Assert.True(result.IsBroken);
        Assert.Equal(1, result.CurrentHp);
    }

    // Third simulated run: 4 of the 8 deaths from a failed test were told as "broken, not dead". The
    // result said isDead true beside currentHp 0 and isBroken false, and a narrator who found the death
    // hard to believe (a haggle, a sneak) read the 0. The result now says it in words, first.

    [Fact]
    public async Task ChallengeCharacter_ThatKills_SaysDeadInWords()
    {
        var random = new Mock<IRandomService>();
        random.Setup(r => r.GenerateRandomRoll(4)).Returns(0);   // the Broken table: dead
        random.Setup(r => r.GenerateRandomRoll(20)).Returns(4);  // a failed test
        var dice = new Dice(random.Object);
        var hero = TestCharacters.Create(dice, maxHp: 1);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools(dice).ChallengeCharacter(
            12, AbilityKind.Presence, 0, ChallengeConsequence.Minor);

        Assert.True(result.IsDead);
        Assert.StartsWith("DEAD", result.Fate);
        Assert.StartsWith("{\"fate\":\"DEAD", JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task ChallengeCharacter_ThatBreaks_SaysBrokenNotDeadAndNamesTheInjury()
    {
        var dice = DiceThatBreakWithALostEye();
        var hero = TestCharacters.Create(dice, maxHp: 1);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools(dice).ChallengeCharacter(
            12, AbilityKind.Strength, 0, ChallengeConsequence.Serious);

        Assert.StartsWith("BROKEN, NOT DEAD", result.Fate);
        Assert.Contains("lost eye", result.Fate);
    }

    [Fact]
    public async Task ChallengeCharacter_ThatNeitherKillsNorBreaks_HasNoFateToTell()
    {
        var dice = DiceThatBreakWithALostEye();
        var hero = TestCharacters.Create(dice, maxHp: 20);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools(dice).ChallengeCharacter(
            12, AbilityKind.Strength, 0, ChallengeConsequence.Serious);

        Assert.Null(result.Fate);
    }

    [Fact]
    public async Task ChallengeCharacter_ThatTakesHours_MovesTheClockAndSaysWhatItReads()
    {
        var hero = BuildHero(_zeroDice);
        var campaign = Campaign.Create(Difficulty.Grim, "Dark", "desc");
        _context.SetCharacterId(hero.Id);
        _context.SetCampaignId(campaign.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        var result = await CharacterTools().ChallengeCharacter(12, AbilityKind.Presence, hoursTaken: 2);

        Assert.Equal(2, campaign.CurrentHour);
        Assert.NotNull(result.TimePassed);
        Assert.Equal((1, 2, false), (result.TimePassed.Day, result.TimePassed.Hour, result.TimePassed.IsNewDawn));
    }

    [Fact]
    public async Task ChallengeCharacter_ThatTakesAMoment_LeavesTheClockAlone()
    {
        var hero = BuildHero(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools().ChallengeCharacter(12, AbilityKind.Presence, hoursTaken: 0);

        Assert.Null(result.TimePassed);
        _campaignsRepo.Verify(r => r.SaveCampaign(It.IsAny<Campaign>()), Times.Never);
    }

    [Theory]
    [InlineData("Food: 3 days")]
    [InlineData("food")]
    [InlineData("a ration")]
    public async Task UseItem_NamingTheFood_EatsADayOfIt(string asTheNarratorWroteIt)
    {
        var hero = TestCharacters.Create(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var result = await CharacterTools().UseItemFromCharacterInventory(asTheNarratorWroteIt);

        Assert.Equal(2, hero.FoodDays);
        Assert.Equal(2, result.Character.FoodDays);
        _charactersRepo.Verify(r => r.Save(hero, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseItem_NamingTheFood_WhenNoneIsLeft_SaysSo()
    {
        var hero = TestCharacters.Create(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);
        for (var day = 0; day < 3; day++) await CharacterTools().UseItemFromCharacterInventory("food");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CharacterTools().UseItemFromCharacterInventory("food"));

        Assert.Contains("no food left", error.Message);
    }

    [Fact]
    public async Task AddScroll_GivesAScrollOfThatSchoolThatCanBeCast()
    {
        var hero = TestCharacters.Create(_zeroDice);
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var found = await CharacterTools().AddScrollToCharacter(ScrollSchool.Sacred);

        var scroll = Assert.Single(hero.Scrolls);
        Assert.Equal(ScrollSchool.Sacred, scroll.School);
        Assert.Equal(found.Name, scroll.Description);
        Assert.Equal(ScrollPowers.Find(scroll.Description)?.Effect, found.Effect);
        Assert.NotNull(found.Effect);
        // What the narrator will then write is the name the tool gave, and CastScroll must know it.
        await CharacterTools().CastScroll(found.Name);
    }

    [Fact]
    public async Task ResolveCombatRound_NamesTheInjuryARetaliationGave()
    {
        var dice = DiceThatBreakWithALostEye();
        var hero = TestCharacters.Create(dice, maxHp: 1);
        var encounter = Encounter.Create("Ambush", "test", EncounterType.Hostile, _zeroDice);
        encounter.AddAdversary(new Adversary("Goblin", new HitPoints(100, 100), new Armor(ArmorTier.None), 7,
            new AttackProfile("Knife", DiceExpr.Parse("1d4"))));
        encounter.StartEncounter();
        _context.SetCharacterId(hero.Id);
        _context.SetActiveEncounterId(encounter.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);
        _encountersRepo.Setup(r => r.Get(encounter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(encounter);

        var round = await EncounterTools(dice).ResolveCombatRound(PlayerRoundAction.Other);

        Assert.Equal("lost eye", Assert.Single(round.Retaliations).Injury);
        Assert.True(round.PlayerBroken);
        Assert.Equal(1, round.PlayerCurrentHp);
    }

    [Fact]
    public async Task CastScroll_NamesTheInjuryAFizzleGave()
    {
        var dice = DiceThatBreakWithALostEye();
        var hero = TestCharacters.Create(dice, maxHp: 1,
            scrolls: new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech"));
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var cast = await CharacterTools(dice).CastScroll("Bestial Speech");

        Assert.False(cast.Succeeded);
        Assert.Equal("lost eye", cast.NewInjury);
    }

    [Fact]
    public async Task CastScroll_FizzleThatKills_ReportsTheDeath()
    {
        var random = new Mock<IRandomService>();
        random.Setup(r => r.GenerateRandomRoll(20)).Returns(1); // Presence test: 2 against DR 12
        var dice = new Dice(random.Object); // every other die rolls 1: the fizzle costs 1, the Broken table says dead
        var hero = TestCharacters.Create(dice, maxHp: 1,
            scrolls: new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech"));
        _context.SetCharacterId(hero.Id);
        _charactersRepo.Setup(r => r.Get(hero.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hero);

        var cast = await CharacterTools(dice).CastScroll("Bestial Speech");

        Assert.False(cast.Succeeded);
        Assert.True(cast.IsDead);
        Assert.Equal(0, cast.CurrentHp);
    }

    // -- CampaignTools --

    [Fact]
    public async Task ConfigureCampaign_UpdatesExistingCampaign()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Default", "default desc");
        _context.SetCampaignId(campaign.Id);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        var result = await CampaignTools().ConfigureCampaign("Doom Awaits", "A slow march to annihilation", "The Drowned Chapel", PoiType.Ruin);

        Assert.Equal("Doom Awaits", result.Name);
        Assert.Equal("A slow march to annihilation", result.Description);
        _campaignsRepo.Verify(r => r.SaveCampaign(campaign), Times.Once);
    }

    [Fact]
    public async Task SetPartyLocation_NeedsTheHoursOfTheJourney()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Dark", "desc");
        campaign.Configure("Doom", "The end", PoiType.Ruin, "The Drowned Chapel");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 50, 20);
        _context.SetCampaignId(campaign.Id);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        await Assert.ThrowsAnyAsync<Exception>(() => CampaignTools().SetPartyLocation("Galgenbeck", hoursTravelled: 0));
        Assert.Equal("The Drowned Chapel", campaign.CurrentLocationName);

        await CampaignTools().SetPartyLocation("Galgenbeck", hoursTravelled: 2);
        Assert.Equal("Galgenbeck", campaign.CurrentLocationName);
        Assert.Equal(2, campaign.CurrentHour);
    }

    [Fact]
    public async Task ConfigureCampaign_AutoStartsWhenPlayerAlreadyJoined()
    {
        var campaign = Campaign.Create(Difficulty.Grim, "Dark", "desc");
        campaign.JoinGame(Guid.NewGuid()); // a player has already joined before setup completes
        _context.SetCampaignId(campaign.Id);
        _campaignsRepo.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);

        await CampaignTools().ConfigureCampaign("Doom Awaits", "A slow march to annihilation", "The Drowned Chapel", PoiType.Ruin);

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
        // The DTO carries no id (the model never sees GUIDs), so the saved aggregate is the witness.
        Encounter? saved = null;
        _encountersRepo
            .Setup(r => r.Save(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()))
            .Callback<Encounter, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        await EncounterTools().CreateEncounter("Goblins", "A goblin ambush", EncounterType.Hostile);

        Assert.NotNull(saved);
        Assert.Equal(saved!.Id, _context.ActiveEncounterId);
    }

    [Fact]
    public async Task CreateEncounter_Unknown_ReturnsRolledReactionInDto()
    {
        // 0-based mock: 3,2 -> 2d6 = 7 -> Indifferent -> Friendly.
        var mock = new Mock<IRandomService>();
        var queue = new Queue<int>([3, 2]);
        mock.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(() => queue.Dequeue());
        var dice = new Dice(mock.Object);

        var result = await EncounterTools(dice).CreateEncounter("Strangers", "Figures in the fog", EncounterType.Unknown);

        Assert.Equal("Friendly", result.Disposition);
        Assert.Equal("Indifferent", result.Reaction);
        Assert.Equal(7, result.ReactionRoll);
    }

    [Fact]
    public async Task CreateEncounter_Hostile_ReportsHostileDispositionWithoutReaction()
    {
        var result = await EncounterTools().CreateEncounter("Ambush", "Bandits leap out", EncounterType.Hostile);

        Assert.Equal("Hostile", result.Disposition);
        Assert.Null(result.Reaction);
        Assert.Null(result.ReactionRoll);
    }

    [Fact]
    public async Task CreateEncounter_NumericType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => EncounterTools().CreateEncounter("Strangers", "Figures in the fog", (EncounterType)5));
    }

    // -- Model-facing binding (the AIFunction the agent actually calls) --

    private AIFunction EncounterFunction(string name, Dice? dice = null) =>
        AIFunctionFactory.Create(typeof(EncounterTools).GetMethod(name)!, EncounterTools(dice));

    [Theory]
    [InlineData(nameof(Engine.GameTools.EncounterTools.CreateEncounter), "initialEncounterType", new[] { "Friendly", "Hostile", "Unknown" })]
    [InlineData(nameof(Engine.GameTools.EncounterTools.ResolveCombatRound), "action", new[] { "Attack", "Flee", "Other" })]
    public void EnumParameters_AreConstrainedInTheToolSchema(string tool, string parameter, string[] expected)
    {
        var schema = EncounterFunction(tool).JsonSchema;

        var values = schema.GetProperty("properties").GetProperty(parameter).GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal(expected, values);
    }

    // Model arguments arrive as JsonElements parsed from the provider's tool-call JSON.
    private static AIFunctionArguments ModelArgs(object args) =>
        new(JsonSerializer.SerializeToElement(args).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));

    [Fact]
    public async Task CreateEncounter_BindsEnumNameFromModelArguments()
    {
        var result = await EncounterFunction(nameof(Engine.GameTools.EncounterTools.CreateEncounter)).InvokeAsync(
            ModelArgs(new { name = "Ambush", description = "Bandits leap out", initialEncounterType = "Hostile" }),
            TestContext.Current.CancellationToken);

        Assert.NotNull(_context.ActiveEncounterId);
        Assert.Contains("Hostile", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CreateEncounter_UndefinedNumericTypeFromModel_FailsWithReadableError()
    {
        // The string-enum binder accepts integers, which the schema cannot rule out.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            EncounterFunction(nameof(Engine.GameTools.EncounterTools.CreateEncounter)).InvokeAsync(
                ModelArgs(new { name = "Strangers", description = "Figures in the fog", initialEncounterType = 5 }),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected one of: Friendly, Hostile, Unknown", ex.Message);
        Assert.Null(_context.ActiveEncounterId);
    }

    [Fact]
    public async Task CreateEncounter_UnknownNameFromModel_FailsBeforeTheDomainRuns()
    {
        // Outside the schema's enum: the binder rejects it, naming the expected type.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            EncounterFunction(nameof(Engine.GameTools.EncounterTools.CreateEncounter)).InvokeAsync(
                ModelArgs(new { name = "Strangers", description = "Figures in the fog", initialEncounterType = "Bogus" }),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("EncounterType", ex.Message);
        Assert.Null(_context.ActiveEncounterId);
    }

    [Fact]
    public async Task ResolveCombatRound_UndefinedActionFromModel_FailsWithReadableError()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            EncounterFunction(nameof(Engine.GameTools.EncounterTools.ResolveCombatRound)).InvokeAsync(
                ModelArgs(new { action = 7 }),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Expected one of: Attack, Flee, Other", ex.Message);
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

        var result = await EncounterTools(hitDice).ResolveCombatRound(PlayerRoundAction.Attack, "Ragged Bandit");

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

        var result = await EncounterTools().ResolveCombatRound(PlayerRoundAction.Other);

        Assert.Null(result.PlayerAttack);
        Assert.Single(result.Retaliations);
        Assert.Equal("Thug", result.Retaliations[0].AdversaryName);
    }

    [Fact]
    public async Task CompleteResolution_ResolvesEncounterAndClearsContext()
    {
        var encounter = CreateEndedUnresolvedEncounter();
        _context.SetActiveEncounterId(encounter.Id);
        _encountersRepo.Setup(r => r.Get(encounter.Id)).ReturnsAsync(encounter);

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

    // -- helpers --

    /// <summary>Sword-armed hero via the shared builder (consumes one d4 roll at construction).</summary>
    private static Character BuildHero(Dice dice) => TestCharacters.Create(dice);

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

    private Encounter CreateEndedUnresolvedEncounter()
    {
        var encounter = Encounter.Create("Test", "test", EncounterType.Hostile, _zeroDice);
        var adversary = NewAdversary("Goblin");
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        adversary.ReceiveDamage(1000);
        encounter.EndEncounter();
        return encounter;
    }
}
