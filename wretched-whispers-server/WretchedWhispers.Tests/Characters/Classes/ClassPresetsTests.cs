using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Dices;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Classes;

public class ClassPresetsTests
{
    /// <summary>The invariant the whole feature rests on: Classless must resolve to the numbers character
    /// creation used before classes existed, so every pre-class character and test behaves identically.</summary>
    [Fact]
    public void Classless_ReproducesThePreClassNumbers()
    {
        var settings = ClassPresets.For(CharacterClass.Classless);

        Assert.Equal(0, settings.StrengthBonus);
        Assert.Equal(0, settings.AgilityBonus);
        Assert.Equal(0, settings.PresenceBonus);
        Assert.Equal(0, settings.ToughnessBonus);
        Assert.Equal(DiceExpr.D8, settings.HpDie);
        Assert.Equal(DiceExpr.D2, settings.OmenDie);
        Assert.Equal(DiceExpr.D10, settings.WeaponDie);
        Assert.Equal(DiceExpr.D4, settings.ArmorDie);
        Assert.Equal(DiceExpr.D(2, 6), settings.SilverDice);
        Assert.True(settings.CanUseScrolls);
        Assert.Null(settings.NaturalWeapon);
        Assert.Null(settings.StartingScrollSchool);
        Assert.Equal(0, settings.StartingScrollCount);
    }

    /// <summary>An empty note is the signal for "emit no class section in the prompt", which keeps prompts
    /// for already-saved classless characters byte-identical.</summary>
    [Fact]
    public void Classless_HasNoNarratorNote()
    {
        Assert.Equal("", ClassPresets.For(CharacterClass.Classless).NarratorNote);
    }

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryClass_HasAPreset(CharacterClass characterClass)
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

    /// <summary>The web sends nothing but the DisplayName to the character sheet, and picks the class
    /// glyph by stripping its spaces back to the enum name. Renaming a DisplayName to anything else
    /// silently drops the glyph, so pin the relationship here where the rename would happen.</summary>
    [Theory]
    [MemberData(nameof(AllClasses))]
    public void DisplayName_IsTheEnumNameWithSpaces(CharacterClass characterClass)
    {
        // Classless is exempt: the API omits the class entirely for it, so its "Classless Scum"
        // display name never reaches the client and never has to resolve to a glyph.
        if (characterClass == CharacterClass.Classless) return;

        var displayName = ClassPresets.For(characterClass).DisplayName;

        Assert.Equal(characterClass.ToString(), displayName.Replace(" ", ""));
    }

    [Fact]
    public void Rollable_IsEveryClassExceptClassless()
    {
        Assert.DoesNotContain(CharacterClass.Classless, ClassPresets.Rollable);
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
