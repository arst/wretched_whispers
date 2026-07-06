using Xunit;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class CharacterRoundTripTests : TestBase
{
    private readonly SqliteTestBase _db;
    private readonly SqliteCharactersRepository _repo;

    public CharacterRoundTripTests()
    {
        _db = new SqliteTestBase();
        _repo = new SqliteCharactersRepository(_db.Db, _db.JsonOptions);
    }

    public override void Dispose()
    {
        _db.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task Save_Then_Get_ReturnsCharacterWithMatchingState()
    {
        SetupDiceRolls(3); // PowerPool.Create
        var abilities = new Abilities(
            new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(0), new AbilityScore(-1));

        var character = Character.Create(
            Guid.NewGuid(), "SavedHero", 10, abilities,
            new StartingEquipment(50, 3, "Sack", null, null,
                Weapon.Create(WeaponKind.Sword),
                new Armor(ArmorTier.Light), null, []), Dice);

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.Equal(character.Id, loaded.Id);
        Assert.Equal(character.Name, loaded.Name);
        Assert.Equal(character.Hp.Current, loaded.Hp.Current);
        Assert.Equal(character.Hp.Max, loaded.Hp.Max);
        Assert.Equal(character.Abilities.Agility.Modifier, loaded.Abilities.Agility.Modifier);
        Assert.Equal(character.Abilities.Presence.Modifier, loaded.Abilities.Presence.Modifier);
        Assert.Equal(character.Abilities.Strength.Modifier, loaded.Abilities.Strength.Modifier);
        Assert.Equal(character.Abilities.Toughness.Modifier, loaded.Abilities.Toughness.Modifier);
        Assert.Equal(character.Silver, loaded.Silver);
        Assert.Equal(character.FoodDays, loaded.FoodDays);
        Assert.Equal(character.Weapon.Kind, loaded.Weapon.Kind);
        Assert.Equal(ArmorTier.Light, loaded.Armor.Tier);
    }

    [Fact]
    public async Task Save_Modify_Save_Get_ReturnsUpdatedState()
    {
        SetupDiceRolls(3); // PowerPool.Create
        var abilities = new Abilities(
            new AbilityScore(0), new AbilityScore(0),
            new AbilityScore(0), new AbilityScore(0));

        var character = Character.Create(
            Guid.NewGuid(), "MutableHero", 10, abilities,
            new StartingEquipment(0, 1, "Sack", null, null,
                Weapon.Create(WeaponKind.Knife),
                new Armor(ArmorTier.None), null, []), Dice);

        await _repo.Save(character);

        // Modify character
        character.Infect();

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.True(loaded.IsInfected);
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var loaded = await _repo.Get(Guid.NewGuid());
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_CharacterWithInjuries_RoundTripsInjurySet()
    {
        // Dice sequence (values are 0-based, actual roll = 1 + value):
        // 1. PowerPool.Create: returns 3 -> roll 4
        // 2. Defend -> ResolveDefence -> Challenge D20: returns 4 -> roll 5 (fail)
        // 3. CalculateDamageAfterDefense -> attack D4: returns 0 -> roll 1 (1 damage)
        // 4. ReceiveDamage -> ResolveBroken -> D4: returns 2 -> roll 3 (proceed to injury)
        // 5. ResolveBroken -> D6: returns 4 -> roll 5 (BrokenHand)
        SetupDiceRolls(3, 4, 0, 2, 4);

        var abilities = new Abilities(
            new AbilityScore(0), new AbilityScore(0),
            new AbilityScore(0), new AbilityScore(0));

        var character = Character.Create(
            Guid.NewGuid(), "InjuredPersistence", 1, abilities,
            new StartingEquipment(0, 1, "Sack", null, null,
                Weapon.Create(WeaponKind.Knife),
                new Armor(ArmorTier.None), null, []), Dice);

        // Force injury
        character.Defend(DiceExpr.D4, Dice);

        Assert.True(character.Injuries.Has(InjuryKind.BrokenHand));

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.True(loaded.Injuries.Has(InjuryKind.BrokenHand));
        Assert.True(loaded.HasBrokenHand);
        Assert.False(loaded.HasLostEye);
        Assert.False(loaded.HasSeveredArm);
        Assert.Equal(character.Hp.Current, loaded.Hp.Current);
    }

    [Fact]
    public async Task Save_FullEquipmentLoadout_RoundTripsCorrectly()
    {
        SetupDiceRolls(3); // PowerPool.Create
        var abilities = new Abilities(
            new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(1), new AbilityScore(0));

        var scrolls = new List<Scroll>
        {
            new(Guid.NewGuid(), ScrollSchool.Sacred, "Heal"),
            new(Guid.NewGuid(), ScrollSchool.Unclean, "Curse")
        };

        var gear1 = new InventoryItem(Guid.NewGuid(), "Rope 30ft", false, false);
        var gear2 = new InventoryItem(Guid.NewGuid(), "Torches", false, true, 4);

        var character = Character.Create(
            Guid.NewGuid(), "FullLoadout", 12, abilities,
            new StartingEquipment(100, 5, "Backpack",
                gear1, gear2,
                Weapon.Create(WeaponKind.Zweihander),
                new Armor(ArmorTier.Heavy),
                new Shield(), scrolls), Dice);

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.Equal(character.Weapon.Kind, loaded.Weapon.Kind);
        Assert.Equal(ArmorTier.Heavy, loaded.Armor.Tier);
        Assert.NotNull(loaded.Shield);
        Assert.Equal(2, loaded.Scrolls.Count);
        Assert.Equal(character.Inventory.InventoryItems.Count, loaded.Inventory.InventoryItems.Count);
        Assert.Equal(character.Powers.MaxUses, loaded.Powers.MaxUses);
        Assert.Equal(character.Powers.UsesRemaining, loaded.Powers.UsesRemaining);
    }
}
