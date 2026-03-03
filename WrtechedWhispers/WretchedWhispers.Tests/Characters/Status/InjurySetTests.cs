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

    [Fact]
    public void Add_SameInjuryTwice_IsIdempotent()
    {
        var set1 = new InjurySet().Add(InjuryKind.LostEye);
        var set2 = set1.Add(InjuryKind.LostEye);

        Assert.Equal(set1, set2);
        Assert.True(set2.Has(InjuryKind.LostEye));
    }

    [Fact]
    public void Add_DoesNotAffectOtherInjuries()
    {
        var set = new InjurySet().Add(InjuryKind.LostEye);

        Assert.False(set.Has(InjuryKind.BrokenHand));
        Assert.False(set.Has(InjuryKind.SeveredArm));
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

    [Fact]
    public void GetPresencePenaltyDice_SmashedFace_ReturnsD4()
    {
        var set = new InjurySet().Add(InjuryKind.SmashedFace);

        Assert.Equal(DiceExpr.D4, set.GetPresencePenaltyDice());
    }

    [Fact]
    public void GetPresencePenaltyDice_NoSmashedFace_ReturnsZeroDice()
    {
        var set = new InjurySet().Add(InjuryKind.BrokenHand);

        Assert.Equal(DiceExpr.Zero, set.GetPresencePenaltyDice());
    }

    [Fact]
    public void GetAgilityPenaltyDice_LostEye_ReturnsD4()
    {
        var set = new InjurySet().Add(InjuryKind.LostEye);

        Assert.Equal(DiceExpr.D4, set.GetAgilityPenaltyDice());
    }

    [Fact]
    public void GetAgilityPenaltyDice_NoLostEye_ReturnsZeroDice()
    {
        var set = new InjurySet().Add(InjuryKind.StabbedLung);

        Assert.Equal(DiceExpr.Zero, set.GetAgilityPenaltyDice());
    }

    [Theory]
    [InlineData(InjuryKind.None)]
    [InlineData(InjuryKind.LostEye)]
    [InlineData(InjuryKind.StabbedLung)]
    [InlineData(InjuryKind.BrokenHand)]
    [InlineData(InjuryKind.CrushedFoot)]
    [InlineData(InjuryKind.SeveredArm)]
    [InlineData(InjuryKind.SmashedFace)]
    public void GetToughnessPenalty_AlwaysReturnsZero(InjuryKind injury)
    {
        // No injury from the broken table penalizes Toughness in Mork Borg
        var set = new InjurySet().Add(injury);

        Assert.Equal(0, set.GetToughnessPenalty());
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
        Assert.True(deserialized.Has(InjuryKind.LostEye));
        Assert.True(deserialized.Has(InjuryKind.SeveredArm));
    }

    [Fact]
    public void Serialization_SerializesAsInteger()
    {
        var set = new InjurySet(InjuryKind.LostEye | InjuryKind.BrokenHand);

        var json = JsonSerializer.Serialize(set);

        // InjuryKind.LostEye = 1, BrokenHand = 4, so combined = 5
        Assert.Contains("5", json);
    }

    [Fact]
    public void DefaultConstructor_HasNoInjuries()
    {
        var set = new InjurySet();

        Assert.Equal(InjuryKind.None, set.Injuries);
    }
}
