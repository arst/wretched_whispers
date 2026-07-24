using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Infrastructure;
using Moq;
using Xunit;
// Disambiguate from the sibling test namespace WretchedWhispers.Tests.Characters.Abilities.
using AbilitySet = WretchedWhispers.Core.Characters.Abilities.Abilities;

namespace WretchedWhispers.Tests.Characters.Combat;

/// <summary>
/// Audits the player attack hit mechanics (Phase 2d). The "near-zero hit rate" reported during
/// the autonomous-combat era turns out NOT to be a domain bug: a MORK BORG DR12 melee test at +0
/// Strength hits ~45% of the time. These tests pin that, so any future regression in the hit math
/// is caught. (The original symptom was the agent fabricating combat / looping, fixed in Phase 2a.)
/// </summary>
public class AttackHitRateTests
{
    private static Character CreateHero(Dice dice, int strengthModifier = 0)
    {
        // Abilities ctor order is (agility, presence, strength, toughness). Sword is melee => Strength.
        var abilities = new AbilitySet(
            agility: new AbilityScore(0),
            presence: new AbilityScore(0),
            strength: new AbilityScore(strengthModifier),
            toughness: new AbilityScore(0));
        var equipment = new StartingEquipment(
            Silver: 10, FoodDays: 3, Container: "backpack (7 items)",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Sword), // melee => uses Strength
            Armor: new Armor(ArmorTier.None),
            Shield: null, Scrolls: []);
        return Character.Create(Guid.NewGuid(), "TestHero", 20, abilities, equipment, dice);
    }

    [Fact]
    public void MeleeAttack_AtZeroStrength_HitsAboutFortyFivePercent()
    {
        // Seeded for determinism; the seed is arbitrary but fixed so the assertion is stable.
        var dice = new Dice(new SeededRandomService(20260608));
        var hero = CreateHero(dice);

        const int trials = 4000;
        var hits = 0;
        for (var i = 0; i < trials; i++)
        {
            var outcome = hero.Attack(new Armor(ArmorTier.None), dice);
            if (outcome.Hit) hits++;
        }

        var rate = (double)hits / trials;

        // DR12 at +0: rolls 12-19 hit (8/20) + natural 20 (1/20) = 9/20 = 45%.
        // A near-zero rate (the original bug report) would fail here.
        Assert.InRange(rate, 0.38, 0.52);
    }

    [Fact]
    public void MeleeAttack_AtHighStrength_HitsMoreOften()
    {
        var dice = new Dice(new SeededRandomService(424242));
        var strongHero = CreateHero(dice, strengthModifier: 3);

        const int trials = 4000;
        var hits = 0;
        for (var i = 0; i < trials; i++)
        {
            var outcome = strongHero.Attack(new Armor(ArmorTier.None), dice);
            if (outcome.Hit) hits++;
        }

        var rate = (double)hits / trials;

        // DR12 at +3: rolls 9-19 hit (11/20) + nat 20 = 12/20 = 60%.
        Assert.InRange(rate, 0.53, 0.67);
    }

    // Deterministic dice so the damage breakdown is auditable.
    private static Dice ScriptedDice(params int[] zeroBasedRolls)
    {
        var queue = new Queue<int>(zeroBasedRolls);
        var random = new Mock<IRandomService>();
        random.Setup(r => r.GenerateRandomRoll(It.IsAny<int>()))
            .Returns(() => queue.Count > 0 ? queue.Dequeue() : 0);
        return new Dice(random.Object);
    }

    [Fact]
    public void Attack_ExposesDamageBreakdown_OnNonCriticalHit()
    {
        // Sword is d6. Creation dice are irrelevant (explicit stats/gear); combat dice are scripted.
        var hero = CreateHero(new Dice(new SeededRandomService(1)));
        // d20 -> 15 (hit, no crit), d6 -> 4 (base damage), NoArmor reduction rolls nothing.
        var outcome = hero.Attack(new Armor(ArmorTier.None), ScriptedDice(14, 3));

        Assert.True(outcome.Hit);
        Assert.False(outcome.Critical);
        Assert.Equal(4, outcome.BaseDamageRoll);
        Assert.Equal(0, outcome.DamageReduction);
        Assert.Equal(4, outcome.Damage.Amount); // base * 1 - 0
    }

    [Fact]
    public void Attack_DoublesBaseRoll_OnCritical()
    {
        var hero = CreateHero(new Dice(new SeededRandomService(1)));
        // d20 -> 20 (natural crit), d6 -> 5 (base damage).
        var outcome = hero.Attack(new Armor(ArmorTier.None), ScriptedDice(19, 4));

        Assert.True(outcome.Critical);
        Assert.Equal(5, outcome.BaseDamageRoll);
        Assert.Equal(10, outcome.Damage.Amount); // base 5 * 2 - 0
    }
}
