using Xunit;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

/// <summary>
/// The repository is JsonSerializer + one EF blob column, and serialization itself is covered by
/// <see cref="JsonSerializationTests"/> — the only repository-specific branch is the update path.
/// </summary>
public class CharacterRoundTripTests : TestBase, IDisposable
{
    private readonly SqliteTestBase _db;
    private readonly SqliteCharactersRepository _repo;

    public CharacterRoundTripTests()
    {
        _db = new SqliteTestBase();
        _repo = new SqliteCharactersRepository(_db.Db, _db.JsonOptions);
    }

    public void Dispose() => _db.Dispose();

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

        character.Infect();

        await _repo.Save(character);
        var loaded = await _repo.Get(character.Id);

        Assert.NotNull(loaded);
        Assert.True(loaded.IsInfected);
    }
}
