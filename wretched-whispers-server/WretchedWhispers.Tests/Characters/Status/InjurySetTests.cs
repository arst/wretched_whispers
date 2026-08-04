using System.Text.Json;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Status;

public class InjurySetTests
{
    [Fact]
    public void Has_EmptyInjurySet_ReturnsFalseForAnyInjury()
    {
        var set = new InjurySet();

        Assert.False(set.Has(InjuryKind.LostEye));
        Assert.False(set.Has(InjuryKind.StabbedLung));
        Assert.False(set.Has(InjuryKind.BrokenHand));
        Assert.False(set.Has(InjuryKind.CrushedFoot));
        Assert.False(set.Has(InjuryKind.SeveredArm));
        Assert.False(set.Has(InjuryKind.SmashedFace));
    }

    [Fact]
    public void Add_SingleInjury_HasReturnsTrue()
    {
        var set = new InjurySet().Add(InjuryKind.LostEye);

        Assert.True(set.Has(InjuryKind.LostEye));
    }

    [Fact]
    public void Add_MultipleInjuries_HasReturnsTrueForBoth()
    {
        var set = new InjurySet()
            .Add(InjuryKind.LostEye)
            .Add(InjuryKind.BrokenHand);

        Assert.True(set.Has(InjuryKind.LostEye));
        Assert.True(set.Has(InjuryKind.BrokenHand));
    }

    [Theory]
    [InlineData(InjuryKind.SeveredArm, 4)]
    [InlineData(InjuryKind.BrokenHand, 2)]
    [InlineData(InjuryKind.LostEye, 0)]
    [InlineData(InjuryKind.StabbedLung, 0)]
    [InlineData(InjuryKind.CrushedFoot, 0)]
    [InlineData(InjuryKind.SmashedFace, 0)]
    [InlineData(InjuryKind.None, 0)]
    public void GetStrengthPenalty_SingleInjury_ReturnsExpectedValue(InjuryKind injury, int expected)
    {
        var set = new InjurySet().Add(injury);

        Assert.Equal(expected, set.GetStrengthPenalty());
    }

    [Fact]
    public void GetStrengthPenalty_SeveredArmAndBrokenHand_SeveredArmDominates()
    {
        // SeveredArm = +4 DR, BrokenHand = +2 DR. SeveredArm dominates (max, not sum).
        var set = new InjurySet()
            .Add(InjuryKind.SeveredArm)
            .Add(InjuryKind.BrokenHand);

        Assert.Equal(4, set.GetStrengthPenalty());
    }

    [Theory]
    [InlineData(InjuryKind.StabbedLung, 2)]
    [InlineData(InjuryKind.CrushedFoot, 2)]
    [InlineData(InjuryKind.SeveredArm, 0)]
    [InlineData(InjuryKind.BrokenHand, 0)]
    [InlineData(InjuryKind.LostEye, 0)]
    [InlineData(InjuryKind.SmashedFace, 0)]
    [InlineData(InjuryKind.None, 0)]
    public void GetAgilityPenalty_SingleInjury_ReturnsExpectedValue(InjuryKind injury, int expected)
    {
        var set = new InjurySet().Add(injury);

        Assert.Equal(expected, set.GetAgilityPenalty());
    }

    [Fact]
    public void GetAgilityPenalty_StabbedLungAndCrushedFoot_ReturnsMaxNotSum()
    {
        // Both give +2 DR independently. Per existing code they are in same case branch,
        // so max is 2, not 4.
        var set = new InjurySet()
            .Add(InjuryKind.StabbedLung)
            .Add(InjuryKind.CrushedFoot);

        Assert.Equal(2, set.GetAgilityPenalty());
    }

    [Theory]
    [InlineData(InjuryKind.SmashedFace, true)]
    [InlineData(InjuryKind.BrokenHand, false)]
    public void GetPresencePenaltyDice_D4OnlyWithSmashedFace(InjuryKind injury, bool expectD4)
    {
        var set = new InjurySet().Add(injury);

        Assert.Equal(expectD4 ? DiceExpr.D4 : DiceExpr.Zero, set.GetPresencePenaltyDice());
    }

    [Theory]
    [InlineData(InjuryKind.LostEye, true)]
    [InlineData(InjuryKind.StabbedLung, false)]
    public void GetAgilityPenaltyDice_D4OnlyWithLostEye(InjuryKind injury, bool expectD4)
    {
        var set = new InjurySet().Add(injury);

        Assert.Equal(expectD4 ? DiceExpr.D4 : DiceExpr.Zero, set.GetAgilityPenaltyDice());
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesInjuries()
    {
        var original = new InjurySet()
            .Add(InjuryKind.LostEye)
            .Add(InjuryKind.SeveredArm);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<InjurySet>(json);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Serialization_SerializesAsInteger()
    {
        var set = new InjurySet(InjuryKind.LostEye | InjuryKind.BrokenHand);

        // InjuryKind.LostEye = 1, BrokenHand = 4, so combined = 5
        Assert.Equal("{\"Injuries\":5}", JsonSerializer.Serialize(set));
    }
}
