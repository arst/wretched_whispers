using WretchedWhispers.Core.Characters.Abilities;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Abilities;

public class AbilityScoreTests
{
    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(6)]
    public void Constructor_ValidValues_DoesNotThrow(int value)
    {
        var score = new AbilityScore(value);
        Assert.Equal(value, score.Modifier);
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(7)]
    public void Constructor_InvalidValues_Throws(int value)
    {
        Assert.Throws<InvalidOperationException>(() => new AbilityScore(value));
    }

    [Theory]
    [InlineData(-3, "-3")]
    [InlineData(0, "+0")]
    [InlineData(2, "+2")]
    [InlineData(6, "+6")]
    public void ToString_ReturnsExpectedFormat(int value, string expected)
    {
        var score = new AbilityScore(value);
        Assert.Equal(expected, score.ToString());
    }
}
