using WretchedWhispers.Core.Characters.Abilities;
using Xunit;
// Disambiguate from the sibling test namespace WretchedWhispers.Tests.Characters.Abilities.
using AbilitySet = WretchedWhispers.Core.Characters.Abilities.Abilities;

namespace WretchedWhispers.Tests.Characters.Abilities;

public class AbilitiesTests
{
    // Agility 1, Presence 2, Strength 3, Toughness 4.
    private static AbilitySet Abilities1234 => new(new AbilityScore(1), new AbilityScore(2),
        new AbilityScore(3), new AbilityScore(4));

    [Theory]
    [InlineData(AbilityKind.Agility, 1)]
    [InlineData(AbilityKind.Presence, 2)]
    [InlineData(AbilityKind.Strength, 3)]
    [InlineData(AbilityKind.Toughness, 4)]
    public void Indexer_ReturnsCorrectAbility(AbilityKind kind, int expected)
    {
        Assert.Equal(expected, Abilities1234[kind].Modifier);
    }

    [Fact]
    public void Indexer_ThrowsForInvalidKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Abilities1234[(AbilityKind)999]);
    }

    [Fact]
    public void ModifyAbility_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = Abilities1234;

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

    [Theory]
    [InlineData(AbilityKind.Agility)]
    [InlineData(AbilityKind.Presence)]
    [InlineData(AbilityKind.Strength)]
    [InlineData(AbilityKind.Toughness)]
    public void ModifyAbility_ThrowsWhenResultOutOfRange(AbilityKind kind)
    {
        // Every ability at the +6 cap: any +1 overflows.
        var abilities = new AbilitySet(new AbilityScore(6), new AbilityScore(6),
            new AbilityScore(6), new AbilityScore(6));

        Assert.Throws<InvalidOperationException>(() => abilities.ModifyAbility(kind, 1));
    }

    [Fact]
    public void ModifyAbility_ThrowsForInvalidKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Abilities1234.ModifyAbility((AbilityKind)999, 1));
    }
}
