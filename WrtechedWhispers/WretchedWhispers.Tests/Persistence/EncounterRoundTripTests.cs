using Xunit;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence.Repositories;

namespace WretchedWhispers.Tests.Persistence;

public class EncounterRoundTripTests : TestBase
{
    private readonly SqliteTestBase _db;
    private readonly SqliteEncountersRepository _repo;

    public EncounterRoundTripTests()
    {
        _db = new SqliteTestBase();
        _repo = new SqliteEncountersRepository(_db.Db, _db.JsonOptions);
    }

    public override void Dispose()
    {
        _db.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task Save_Then_Get_ReturnsEncounterWithAdversaries()
    {
        SetupDiceRolls(7, 7); // Reaction roll (2d6)
        var encounter = Encounter.Create("Dark Cave", "A cave full of evil",
            EncounterType.Hostile, Dice);

        var adversary = new Adversary("Goblin",
            new HitPoints(5, 5),
            new Armor(LightArmorTier.Instance),
            7,
            new AttackProfile("Claw", DiceExpr.D4));
        encounter.AddAdversary(adversary);

        await _repo.Save(encounter);
        var loaded = await _repo.Get(encounter.Id);

        Assert.NotNull(loaded);
        Assert.Equal(encounter.Id, loaded.Id);
        Assert.Equal(encounter.InitialType, loaded.InitialType);
        Assert.Equal(encounter.Name, loaded.Name);
        Assert.Single(loaded.Adversaries);
        Assert.Equal("Goblin", loaded.Adversaries[0].Name);
        Assert.Equal(5, loaded.Adversaries[0].Hp.Current);
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var loaded = await _repo.Get(Guid.NewGuid());
        Assert.Null(loaded);
    }
}
