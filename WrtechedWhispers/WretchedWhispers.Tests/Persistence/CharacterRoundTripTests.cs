using Xunit;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
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
                new Armor(LightArmorTier.Instance), null, []), Dice);

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
        Assert.IsType<LightArmorTier>(loaded.Armor.Tier);
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
                new Armor(NoArmorTier.Instance), null, []), Dice);

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
                new Armor(HeavyArmorTier.Instance),
                new Shield(), scrolls), Dice);

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.Equal(character.Weapon.Kind, loaded.Weapon.Kind);
        Assert.IsType<HeavyArmorTier>(loaded.Armor.Tier);
        Assert.NotNull(loaded.Shield);
        Assert.Equal(2, loaded.Scrolls.Count);
        Assert.Equal(character.Inventory.InventoryItems.Count, loaded.Inventory.InventoryItems.Count);
        Assert.Equal(character.Powers.MaxUses, loaded.Powers.MaxUses);
        Assert.Equal(character.Powers.UsesRemaining, loaded.Powers.UsesRemaining);
    }
}
