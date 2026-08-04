using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using Xunit;

namespace WretchedWhispers.Tests.Characters.Combat;

/// <summary>Audits the player attack damage breakdown with scripted dice.</summary>
public class AttackHitRateTests : TestBase
{
    [Fact]
    public void Attack_ExposesDamageBreakdown_OnNonCriticalHit()
    {
        // Sword is d6. Creation consumes one power d4; then d20 -> 15 (hit, no crit),
        // d6 -> 4 (base damage), NoArmor reduction rolls nothing.
        SetupDiceRolls(0 /* creation power d4 */, 14 /* d20 */, 3 /* d6 */);
        var hero = TestCharacters.Create(Dice);

        var outcome = hero.Attack(new Armor(ArmorTier.None), Dice);

        Assert.True(outcome.Hit);
        Assert.False(outcome.Critical);
        Assert.Equal(4, outcome.BaseDamageRoll);
        Assert.Equal(0, outcome.DamageReduction);
        Assert.Equal(4, outcome.Damage.Amount); // base * 1 - 0
    }

    [Fact]
    public void Attack_DoublesBaseRoll_OnCritical()
    {
        // Creation power d4; then d20 -> 20 (natural crit), d6 -> 5 (base damage).
        SetupDiceRolls(0 /* creation power d4 */, 19 /* d20 */, 4 /* d6 */);
        var hero = TestCharacters.Create(Dice);

        var outcome = hero.Attack(new Armor(ArmorTier.None), Dice);

        Assert.True(outcome.Critical);
        Assert.Equal(5, outcome.BaseDamageRoll);
        Assert.Equal(10, outcome.Damage.Amount); // base 5 * 2 - 0
    }
}
