using WretchedWhispers.Core.Characters.Abilities;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Abilities;

public class AbilitiesTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var agi = new AbilityScore(1);
        var pre = new AbilityScore(2);
        var str = new AbilityScore(3);
        var tou = new AbilityScore(4);

        var abilities = new Core.Characters.Abilities.Abilities(agi, pre, str, tou);

        Assert.Equal(agi, abilities.Agility);
        Assert.Equal(pre, abilities.Presence);
        Assert.Equal(str, abilities.Strength);
        Assert.Equal(tou, abilities.Toughness);
    }

    [Theory]
    [InlineData(AbilityKind.Agility, 1)]
    [InlineData(AbilityKind.Presence, 2)]
    [InlineData(AbilityKind.Strength, 3)]
    [InlineData(AbilityKind.Toughness, 4)]
    public void Indexer_ReturnsCorrectAbility(AbilityKind kind, int expected)
    {
        var abilities = new Core.Characters.Abilities.Abilities(new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(3), new AbilityScore(4));
        Assert.Equal(expected, abilities[kind].Modifier);
    }

    [Fact]
    public void Indexer_ThrowsForInvalidKind()
    {
        var abilities = new Core.Characters.Abilities.Abilities(new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(3), new AbilityScore(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => abilities[(AbilityKind)999]);
    }

    [Theory]
    [InlineData(AbilityKind.Agility, 1, 2, 3, 4, AbilityKind.Agility, 5)] // 1+4=5
    [InlineData(AbilityKind.Presence, 1, 2, 3, 4, AbilityKind.Presence, 6)] // 2+4=6
    [InlineData(AbilityKind.Strength, 1, 2, 3, 4, AbilityKind.Strength, 6)] // 3+3=6
    [InlineData(AbilityKind.Toughness, 1, 2, 3, 4, AbilityKind.Toughness, 6)] // 4+2=6
    public void ModifyAbility_ReturnsNewInstanceWithUpdatedAbility(AbilityKind kind, int agi, int pre, int str, int tou,
        AbilityKind checkKind, int expected)
    {
        var abilities = new Core.Characters.Abilities.Abilities(new AbilityScore(agi), new AbilityScore(pre),
            new AbilityScore(str), new AbilityScore(tou));
        var modified = abilities.ModifyAbility(kind, expected - abilities[kind].Modifier);
        Assert.Equal(expected, modified[checkKind].Modifier);
    }

    [Fact]
    public void ModifyAbility_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = new Core.Characters.Abilities.Abilities(
            new AbilityScore(1), new AbilityScore(2), new AbilityScore(3), new AbilityScore(4));

        var modified = original.ModifyAbility(AbilityKind.Strength, 2);

        // Original is unchanged
        Assert.Equal(3, original.Strength.Modifier);
        // Modified has the new value
        Assert.Equal(5, modified.Strength.Modifier);
        // Other abilities are preserved
        Assert.Equal(1, modified.Agility.Modifier);
        Assert.Equal(2, modified.Presence.Modifier);
        Assert.Equal(4, modified.Toughness.Modifier);
    }

    [Fact]
    public void ModifyAbility_ReturnsNewInstance_NotSameReference()
    {
        var original = new Core.Characters.Abilities.Abilities(
            new AbilityScore(1), new AbilityScore(2), new AbilityScore(3), new AbilityScore(4));

        var modified = original.ModifyAbility(AbilityKind.Agility, 1);

        Assert.NotSame(original, modified);
    }

    [Theory]
    [InlineData(AbilityKind.Agility, 6, 0, 0, 0, 1)] // 6+1=7, should throw
    [InlineData(AbilityKind.Presence, 0, 6, 0, 0, 1)] // 6+1=7, should throw
    [InlineData(AbilityKind.Strength, 0, 0, 6, 0, 1)] // 6+1=7, should throw
    [InlineData(AbilityKind.Toughness, 0, 0, 0, 6, 1)] // 6+1=7, should throw
    public void ModifyAbility_ThrowsWhenResultOutOfRange(AbilityKind kind, int agi, int pre, int str, int tou,
        int delta)
    {
        var abilities = new Core.Characters.Abilities.Abilities(new AbilityScore(agi), new AbilityScore(pre),
            new AbilityScore(str), new AbilityScore(tou));
        Assert.Throws<InvalidOperationException>(() => abilities.ModifyAbility(kind, delta));
    }

    [Fact]
    public void ModifyAbility_ThrowsForInvalidKind()
    {
        var abilities = new Core.Characters.Abilities.Abilities(new AbilityScore(1), new AbilityScore(2),
            new AbilityScore(3), new AbilityScore(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => abilities.ModifyAbility((AbilityKind)999, 1));
    }
}
