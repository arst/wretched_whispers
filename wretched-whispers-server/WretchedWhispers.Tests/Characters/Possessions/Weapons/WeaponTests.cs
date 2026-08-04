using WretchedWhispers.Core.Characters.Possessions.Weapons;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Possessions.Weapons;

public class WeaponTests
{
    [Theory]
    [InlineData(WeaponKind.Femur, 4)]
    [InlineData(WeaponKind.Staff, 4)]
    [InlineData(WeaponKind.ShortSword, 4)]
    [InlineData(WeaponKind.Knife, 4)]
    [InlineData(WeaponKind.Warhammer, 6)]
    [InlineData(WeaponKind.Sword, 6)]
    [InlineData(WeaponKind.Bow, 6)]
    [InlineData(WeaponKind.Fangs, 6)]
    [InlineData(WeaponKind.Claws, 6)]
    [InlineData(WeaponKind.Flail, 8)]
    [InlineData(WeaponKind.Crossbow, 8)]
    [InlineData(WeaponKind.Zweihander, 10)]
    [InlineData(WeaponKind.Improvised, 4)]
    public void Create_SetsCorrectKindAndDamageDie(WeaponKind kind, int expectedDie)
    {
        var weapon = Weapon.Create(kind);
        Assert.Equal(kind, weapon.Kind);
        Assert.Equal(expectedDie, weapon.DamageDie.Sides);
    }

    [Theory]
    [InlineData(WeaponKind.Zweihander, true)]
    [InlineData(WeaponKind.Sword, false)]
    [InlineData(WeaponKind.Bow, false)]
    [InlineData(WeaponKind.Fangs, false)]
    [InlineData(WeaponKind.Claws, false)]
    public void IsTwoHanded_OnlyTrueForZweihander(WeaponKind kind, bool expected)
    {
        var weapon = Weapon.Create(kind);
        Assert.Equal(expected, weapon.IsTwoHanded);
    }

    [Theory]
    [InlineData(WeaponKind.Bow, true)]
    [InlineData(WeaponKind.Crossbow, true)]
    [InlineData(WeaponKind.Sword, false)]
    [InlineData(WeaponKind.Zweihander, false)]
    [InlineData(WeaponKind.Fangs, false)]
    [InlineData(WeaponKind.Claws, false)]
    public void IsRanged_TrueOnlyForBowAndCrossbow(WeaponKind kind, bool expected)
    {
        var weapon = Weapon.Create(kind);
        Assert.Equal(expected, weapon.IsRanged);
    }

    [Fact]
    public void Create_UnknownKindDefaultsToImprovised()
    {
        const WeaponKind unknownKind = (WeaponKind)999;
        var weapon = Weapon.Create(unknownKind);
        Assert.Equal(WeaponKind.Improvised, weapon.Kind);
        Assert.Equal(4, weapon.DamageDie.Sides);
    }
}
