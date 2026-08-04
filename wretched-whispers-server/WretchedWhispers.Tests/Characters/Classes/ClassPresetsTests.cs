using WretchedWhispers.Core.Characters.Classes;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Classes;

public class ClassPresetsTests
{
    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryClass_HasSanePresetValues(CharacterClass characterClass)
    {
        var settings = ClassPresets.For(characterClass);

        Assert.False(string.IsNullOrWhiteSpace(settings.DisplayName));
        Assert.True(settings.HpDie.Sides > 0);
        Assert.True(settings.OmenDie.Sides > 0);
        Assert.True(settings.WeaponDie.Sides > 0);
        Assert.True(settings.ArmorDie.Sides > 0);
        Assert.True(settings.SilverDice.Max > 0);
        // A named school with nothing to apply it to is a typo. The reverse is legal: a count with no
        // school means "roll sacred or unclean", which is how the Hermit's one scroll works.
        if (settings.StartingScrollSchool is not null) Assert.True(settings.StartingScrollCount > 0);
    }

    /// <summary>The gear tables only reach as far as the class die: armour tops out at d4 (four tiers) and
    /// the weapon table at d10. A larger die would index past the end and silently fall through.</summary>
    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryClass_RollsWithinTheGearTables(CharacterClass characterClass)
    {
        var settings = ClassPresets.For(characterClass);

        Assert.InRange(settings.WeaponDie.Sides, 2, 10);
        Assert.InRange(settings.ArmorDie.Sides, 2, 4);
    }

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryRealClass_HasANarratorNote(CharacterClass characterClass)
    {
        if (characterClass == CharacterClass.Classless) return;

        Assert.False(string.IsNullOrWhiteSpace(ClassPresets.For(characterClass).NarratorNote));
    }

    /// <summary>Bonuses land on the 3d6 roll, and the published classes never move it by more than 2 in
    /// either direction. A bigger number here is a transcription error, not a design choice.</summary>
    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryClass_HasModestAbilityBonuses(CharacterClass characterClass)
    {
        var settings = ClassPresets.For(characterClass);

        Assert.InRange(settings.StrengthBonus, -2, 2);
        Assert.InRange(settings.AgilityBonus, -2, 2);
        Assert.InRange(settings.PresenceBonus, -2, 2);
        Assert.InRange(settings.ToughnessBonus, -2, 2);
    }

    [Fact]
    public void Rollable_IsEveryClassExceptClassless()
    {
        Assert.Equal(
            Enum.GetValues<CharacterClass>().Where(c => c != CharacterClass.Classless).ToArray(),
            ClassPresets.Rollable);
    }

    public static TheoryData<CharacterClass> AllClasses()
    {
        var data = new TheoryData<CharacterClass>();
        foreach (var characterClass in Enum.GetValues<CharacterClass>()) data.Add(characterClass);
        return data;
    }
}
