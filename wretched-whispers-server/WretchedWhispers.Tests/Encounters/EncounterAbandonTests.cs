using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using Xunit;

namespace WretchedWhispers.Tests.Encounters;

/// <summary>A fight whose player died is over for good. It is closed when the dead are buried, so the
/// successor does not inherit it.</summary>
public sealed class EncounterAbandonTests : TestBase
{
    private Encounter Meeting(EncounterType type)
    {
        var encounter = Encounter.Create("Ambush", "desc", type, Dice);
        encounter.AddAdversary(new Adversary("Ghoul", new HitPoints(8, 8), new Armor(ArmorTier.None), 7,
            new AttackProfile("claws", DiceExpr.Parse("d4"))));
        return encounter;
    }

    [Fact]
    public void Abandon_FromAStartedFight_IsEndedAndResolved()
    {
        var fight = Meeting(EncounterType.Hostile);
        fight.StartEncounter();

        fight.Abandon();

        Assert.True(fight.IsEnded);
        Assert.True(fight.IsResolved);
        // Whoever was still standing stays as they were: nobody won this.
        Assert.False(Assert.Single(fight.Adversaries).IsDead);
    }

    [Fact]
    public void Abandon_FromAMeetingThatNeverCameToBlows_IsResolved()
    {
        var meeting = Meeting(EncounterType.Friendly);

        meeting.Abandon();

        Assert.True(meeting.IsResolved);
    }
}
