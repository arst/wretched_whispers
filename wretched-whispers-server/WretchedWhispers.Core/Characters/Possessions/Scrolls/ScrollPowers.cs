using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Possessions.Scrolls;

/// <summary>How much of a power the game resolves itself. <see cref="Narrative"/> powers change no
/// numbers and are the narrator's to tell; the rest are applied by the domain on a successful cast.</summary>
public enum ScrollEffectKind
{
    Narrative,

    /// <summary>The caster regains <see cref="ScrollPower.Amount"/> hit points.</summary>
    HealCaster,

    /// <summary>The caster's next test is easier by <see cref="ScrollPower.Amount"/>.</summary>
    BlessNextTest,

    /// <summary><see cref="ScrollPower.Targets"/> adversaries (one when unset) each lose
    /// <see cref="ScrollPower.Amount"/>.</summary>
    DamageEach,

    /// <summary>Every adversary standing shares one roll of <see cref="ScrollPower.Amount"/>.</summary>
    DamageSplit
}

/// <summary>One power from the scroll tables. <paramref name="Effect"/> is what the narrator is told the
/// scroll does; <paramref name="Kind"/> says what the game applies on its own.</summary>
public sealed record ScrollPower(
    string Name, ScrollSchool School, string Effect,
    ScrollEffectKind Kind = ScrollEffectKind.Narrative, DiceExpr Amount = default, DiceExpr Targets = default);

/// <summary>The two d10 scroll tables of MORK BORG. A <see cref="Scroll"/> is named for its power and the
/// power is looked up by that name, so a scroll saved before powers existed (named "random") simply has
/// none.</summary>
public static class ScrollPowers
{
    // ponytail: a solo game, so "a creature of your choice" on a helpful power is the caster, and
    // powers that last several rounds are resolved at once (noted on each). Give them a duration when
    // the encounter can carry effects across rounds.
    public static readonly IReadOnlyList<ScrollPower> All =
    [
        new("Palms Open the Southern Gate", ScrollSchool.Unclean,
            "A ball of fire bursts from the caster's palms and burns d2 creatures for d8 each.",
            ScrollEffectKind.DamageEach, DiceExpr.D8, DiceExpr.D2),
        new("Tongue of Eris", ScrollSchool.Unclean,
            "One creature of the caster's choice is lost in confusion for ten minutes."),
        new("Te-le-kin-esis", ScrollSchool.Unclean,
            "The caster moves one object by will alone, up to d10 x 10 feet, for d6 minutes."),
        new("Lucy-fires Levitation", ScrollSchool.Unclean,
            "The caster hovers above the ground for Presence + d10 rounds."),
        // The book bleeds d4 a round for d6 rounds; resolved here as one loss of 2d4.
        new("Daemon of Capillaries", ScrollSchool.Unclean,
            "One creature chokes on its own blood and loses 2d4.",
            ScrollEffectKind.DamageEach, DiceExpr.D(2, 4)),
        new("Nine Violet Signs Unknot the Storm", ScrollSchool.Unclean,
            "Lightning answers: d2 bolts, each striking a creature for d6.",
            ScrollEffectKind.DamageEach, DiceExpr.D6, DiceExpr.D2),
        new("Metzhuotl Blind Your Eye", ScrollSchool.Unclean,
            "One creature turns invisible for d6 rounds, and is far harder to strike or to guard against."),
        new("Foul Psychopomp", ScrollSchool.Unclean,
            "The dead answer the call: d4 skeletons or zombies claw their way up. They obey no one."),
        new("Eyelid Blinds the Mind", ScrollSchool.Unclean,
            "d4 creatures fall asleep for an hour unless they can fight it off."),
        new("Death", ScrollSchool.Unclean,
            "Everything living close by shares 4d10 points of ruin.",
            ScrollEffectKind.DamageSplit, DiceExpr.D(4, 10)),

        new("Grace of a Dead Saint", ScrollSchool.Sacred,
            "A dead saint's mercy closes wounds: the caster regains d10 hit points.",
            ScrollEffectKind.HealCaster, DiceExpr.D10),
        new("Grace for a Sinner", ScrollSchool.Sacred,
            "Fortune bends once: the caster's next test is easier by d6.",
            ScrollEffectKind.BlessNextTest, DiceExpr.D6),
        new("Whispers Pass the Gate", ScrollSchool.Sacred,
            "The caster may ask three questions of one dead creature."),
        // The book grants 2d6 extra hit points for ten rounds; resolved here as 2d6 regained.
        new("Aegis of Sorrow", ScrollSchool.Sacred,
            "A shroud of grief knits the caster together: 2d6 hit points regained.",
            ScrollEffectKind.HealCaster, DiceExpr.D(2, 6)),
        new("Unmet Fate", ScrollSchool.Sacred,
            "One creature dead no more than a week wakes, carrying terrible memories. Never a fallen player character."),
        new("Bestial Speech", ScrollSchool.Sacred,
            "The caster can speak with animals for d20 minutes."),
        new("False Dawn / Night's Chariot", ScrollSchool.Sacred,
            "Light, or pitch darkness, fills the place for 3d10 minutes."),
        new("Hermetic Step", ScrollSchool.Sacred,
            "For 2d10 minutes the caster finds every trap in their path."),
        new("Roskoe's Consuming Glare", ScrollSchool.Sacred,
            "The caster's stare eats at d4 creatures, d8 each.",
            ScrollEffectKind.DamageEach, DiceExpr.D8, DiceExpr.D4),
        new("Enochian Syntax", ScrollSchool.Sacred,
            "One creature blindly obeys a single command.")
    ];

    public static ScrollPower Roll(ScrollSchool school, Dice dice) =>
        All.Where(p => p.School == school).ElementAt(dice.Roll(DiceExpr.D10) - 1);

    public static ScrollPower? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
