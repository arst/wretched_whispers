using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.GameTools.Models;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Challenge;

/// <summary>
/// A test's difficulty is not always the number asked for: armor, a load, an injury, an omen or a
/// blessing move it. The outcome says which, so a narrator explaining "against 14" names the real cause.
/// Seen in an eval: a dodge against 14 in medium armor told as "that extra encumbrance".
/// </summary>
public sealed class DrReasonsTests : TestBase
{
    [Fact]
    public void Defend_InMediumArmor_NamesTheArmor()
    {
        var character = TestCharacters.Create(Dice, armorTier: ArmorTier.Medium);

        var outcome = character.Defend(DiceExpr.D4, Dice);

        Assert.Equal(14, outcome.EffectiveDr);
        Assert.Equal(["armor +2"], outcome.DrReasons);
    }

    [Fact]
    public void Defend_InHeavyArmor_CountsBothOfItsPenaltiesAsArmor()
    {
        var character = TestCharacters.Create(Dice, armorTier: ArmorTier.Heavy);

        var outcome = character.Defend(DiceExpr.D4, Dice);

        Assert.Equal(18, outcome.EffectiveDr);
        Assert.Equal(["armor +6"], outcome.DrReasons);
    }

    [Fact]
    public void Challenge_WithNothingInTheWay_GivesNoReason()
    {
        var character = TestCharacters.Create(Dice);

        var outcome = character.Challenge(new Dr(12), AbilityKind.Agility, Dice);

        Assert.Empty(outcome.DrReasons);
    }

    [Fact]
    public void Challenge_SpendingAnOmen_NamesTheOmen()
    {
        var character = TestCharacters.Create(Dice, startingOmens: 1);

        var outcome = character.Challenge(new Dr(12), AbilityKind.Presence, Dice, spendOmenToLowerDr: true);

        Assert.Equal(8, outcome.EffectiveDr);
        Assert.Equal(["omen -4"], outcome.DrReasons);
    }

    [Fact]
    public void RoundResult_TellsTheNarratorWhyTheDodgeWasHarder()
    {
        var character = TestCharacters.Create(Dice, armorTier: ArmorTier.Medium);
        var defence = character.Defend(DiceExpr.D4, Dice);
        var outcome = new CombatRoundOutcome(
            PlayerAttack: null, PlayerAttackTarget: null, FleeAttempt: null,
            Retaliations: [new AdversaryRetaliation("Priest", defence)],
            AdversariesFledThisRound: [], EncounterEnded: false, EndReason: EncounterEndReason.None,
            PlayerCurrentHp: character.Hp.Current, PlayerBroken: false);

        var dto = CombatRoundOutcomeDto.From(outcome);

        Assert.Equal("armor +2", dto.Retaliations[0].DrBecause);
    }
}
