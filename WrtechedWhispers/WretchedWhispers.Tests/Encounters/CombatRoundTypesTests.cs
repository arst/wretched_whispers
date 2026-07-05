using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Encounters;

public sealed class CombatRoundTypesTests : TestBase
{
    private Encounter CreateStartedEncounter()
    {
        var encounter = Encounter.Create("Test", "desc", EncounterType.Hostile, Dice);
        encounter.AddAdversary(new Adversary(
            "Ghoul", new HitPoints(4, 4), new Armor(ArmorTier.None), 7,
            new AttackProfile("claws", DiceExpr.Parse("d4"))));
        encounter.StartEncounter();
        return encounter;
    }

    [Fact]
    public void EndByPlayerEscape_EndsDespiteActiveAdversaries()
    {
        var encounter = CreateStartedEncounter();

        encounter.EndByPlayerEscape();

        Assert.True(encounter.IsEnded);
    }

    [Fact]
    public void EndByPlayerEscape_NotStarted_Throws()
    {
        var encounter = Encounter.Create("Test", "desc", EncounterType.Hostile, Dice);
        Assert.Throws<InvalidOperationException>(encounter.EndByPlayerEscape);
    }

    [Fact]
    public void AttemptFlee_IsAgilityTestAgainstDr12()
    {
        var character = TestCharacters.Create(Dice);
        SetupDiceRoll(20, 14);

        var outcome = character.AttemptFlee(Dice);

        Assert.Equal(15, outcome.Roll);
        // effective DR = 12 + armor agility penalty (+ any injury/encumbrance adjustments)
        Assert.True(outcome.EffectiveDr >= 12);
    }
}
