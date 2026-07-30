using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Classes;

/// <summary>Maps each class to its settings. The single source of the class numbers.
/// <para>
/// Every number below was checked against the published class stat blocks. What is NOT modelled, and is
/// therefore the narrator's problem: the d6 signature item each class picks at creation, the Gutterborn's
/// specialty, the Herbmaster's decoction list, the Skinwalker's beast form, and the Priest's licence to
/// use Powers in medium armour (<see cref="Possessions.Scrolls.ScrollRestrictionPolicy"/> takes no
/// character, so an exemption would ripple through the whole cast path).
/// </para>
/// <para>
/// Narrator notes are original prose, not rulebook text -- same posture as the Misery psalms in
/// <see cref="Campaigns.World.CalendarOfNechrubel"/>. They describe only what the domain does NOT
/// compute; every quantity lives in the fields above them.
/// </para></summary>
public static class ClassPresets
{
    /// <summary>The classes a random roll can land on -- <see cref="CharacterClass.Classless"/> is a
    /// deliberate choice, never a roll result.</summary>
    public static readonly CharacterClass[] Rollable =
    [
        CharacterClass.FangedDeserter,
        CharacterClass.GutterbornScum,
        CharacterClass.EsotericHermit,
        CharacterClass.OccultHerbmaster,
        CharacterClass.HereticalPriest,
        CharacterClass.CursedSkinwalker
    ];

    public static ClassSettings For(CharacterClass characterClass) => characterClass switch
    {
        // Reproduces the pre-class numbers exactly: no bonuses, Toughness+d8 HP, d2 omens, d10 weapon,
        // d4 armour, 2d6x10 silver, the rolled weapon, no kit. An empty note tells PromptComposer to emit
        // no class section, which keeps prompts for already-saved characters byte-identical.
        CharacterClass.Classless => new ClassSettings(
            DisplayName: "Classless Scum",
            StrengthBonus: 0, AgilityBonus: 0, PresenceBonus: 0, ToughnessBonus: 0,
            HpDie: DiceExpr.D8,
            OmenDie: DiceExpr.D2,
            WeaponDie: DiceExpr.D10,
            ArmorDie: DiceExpr.D4,
            SilverDice: DiceExpr.D(2, 6),
            CanUseScrolls: true,
            NaturalWeapon: null,
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: ""),

        // Illiterate by the book: any scroll they roll is paper to them.
        CharacterClass.FangedDeserter => new ClassSettings(
            DisplayName: "Fanged Deserter",
            StrengthBonus: +2, AgilityBonus: -1, PresenceBonus: -1, ToughnessBonus: 0,
            HpDie: DiceExpr.D10,
            OmenDie: DiceExpr.D2,
            WeaponDie: DiceExpr.D10,
            ArmorDie: DiceExpr.D4,
            SilverDice: DiceExpr.D(2, 6),
            CanUseScrolls: false,
            NaturalWeapon: WeaponKind.Fangs,
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: """
                A FANGED DESERTER: a hulking oath-breaker who walked away from a war nobody was winning.
                Something went wrong in the jaw -- tusks push through the lip and will not stop growing.
                They fight with those fangs; it is the only weapon they trust.
                - Beasts and children give them a wide berth; guards and press-gangs give them a hard look.
                - Old campaigners recognise the walk. Some of them are still owed something.
                - They know soldiering: camps, sieges, how a line breaks, which officers are worth robbing.
                - They cannot read. Scrolls, letters and signage are shapes to them; they never cast.
                """),

        CharacterClass.GutterbornScum => new ClassSettings(
            DisplayName: "Gutterborn Scum",
            StrengthBonus: -2, AgilityBonus: 0, PresenceBonus: 0, ToughnessBonus: 0,
            HpDie: DiceExpr.D6,
            OmenDie: DiceExpr.D2,
            WeaponDie: DiceExpr.D6,
            ArmorDie: DiceExpr.D2,
            SilverDice: DiceExpr.D6,
            CanUseScrolls: true,
            NaturalWeapon: null,
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: """
                GUTTERBORN SCUM: born in the runoff, raised by nobody, overlooked by everyone -- which has
                kept them breathing longer than their betters. Fortune owes them, and they collect often.
                - They know the underside of any settlement: which roof connects, which gutter drains where,
                  who fences stolen goods and who informs.
                - Nobody remembers their face. Servants, beggars and dogs talk to them freely.
                - Slipping notice is their whole trade -- in muck, crowds or clutter, judge attempts to
                  spot them harshly, and let them go unremarked where anyone else would be challenged.
                - They hoard worthless things because once, one of them was not worthless.
                """),

        CharacterClass.EsotericHermit => new ClassSettings(
            DisplayName: "Esoteric Hermit",
            StrengthBonus: -2, AgilityBonus: 0, PresenceBonus: +2, ToughnessBonus: 0,
            HpDie: DiceExpr.D4,
            OmenDie: DiceExpr.D4,
            WeaponDie: DiceExpr.D4,
            ArmorDie: DiceExpr.D2,
            SilverDice: DiceExpr.D6,
            CanUseScrolls: true,
            NaturalWeapon: null,
            StartingScrollSchool: null,
            StartingScrollCount: 1,
            NarratorNote: """
                An ESOTERIC HERMIT: years alone in a hole in the waste, reading what should have stayed
                buried. The reading took, and left the body behind: they break easily and carry nothing heavy.
                - They recognise occult marks, ruined shrines, and the names of things better left unnamed.
                - Company is an ordeal. Crowds, courtesies and small talk visibly cost them.
                - They speak of the world's end as scheduled rather than feared.
                """),

        CharacterClass.OccultHerbmaster => new ClassSettings(
            DisplayName: "Occult Herbmaster",
            StrengthBonus: -2, AgilityBonus: 0, PresenceBonus: 0, ToughnessBonus: +2,
            HpDie: DiceExpr.D6,
            OmenDie: DiceExpr.D2,
            WeaponDie: DiceExpr.D6,
            ArmorDie: DiceExpr.D2,
            SilverDice: DiceExpr.D(2, 6),
            CanUseScrolls: true,
            NaturalWeapon: null,
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: """
                An OCCULT HERBMASTER: a poisoner with better manners, who reads rot and root the way a
                priest reads scripture. Their stomach has survived every experiment so far.
                - They can identify plants, fungi and filth on sight, and say what it does to a body.
                - Given time, quiet and real ingredients they can brew. Gathering or brewing anything is an
                  ACTION with a tool call behind it -- never let a described brew become a usable item on its own.
                - Apothecaries, poisoners and cautious nobles all have reasons to know their name.
                """),

        CharacterClass.HereticalPriest => new ClassSettings(
            DisplayName: "Heretical Priest",
            StrengthBonus: -2, AgilityBonus: 0, PresenceBonus: +2, ToughnessBonus: 0,
            HpDie: DiceExpr.D8,
            OmenDie: DiceExpr.D4,
            WeaponDie: DiceExpr.D8,
            ArmorDie: DiceExpr.D4,
            SilverDice: DiceExpr.D(3, 6),
            CanUseScrolls: true,
            NaturalWeapon: null,
            // No free scroll: the priest's edge is omens, silver and a signature relic, not a starting spell.
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: """
                A HERETICAL PRIEST: ordained, then cast out for preaching the wrong end of the world. Still
                wears the vestments. Still believes -- just not what the church would like.
                - They know liturgy, feast days, and the layout and hierarchy of any temple.
                - The faithful react badly and the clergy worse; both recognise what they used to be.
                - They will argue theology with anything, including things that are eating them.
                """),

        // The claws are a stand-in, not a rule: by the book the Skinwalker rolls an ordinary weapon and
        // grows the beast's when it shifts. Without a shift mechanic they would have no class weapon at
        // all, so they wear the wolf's d6 permanently.
        CharacterClass.CursedSkinwalker => new ClassSettings(
            DisplayName: "Cursed Skinwalker",
            StrengthBonus: +1, AgilityBonus: 0, PresenceBonus: -2, ToughnessBonus: +1,
            HpDie: DiceExpr.D8,
            OmenDie: DiceExpr.D2,
            WeaponDie: DiceExpr.D6,
            ArmorDie: DiceExpr.D2,
            SilverDice: DiceExpr.D(2, 6),
            CanUseScrolls: true,
            NaturalWeapon: WeaponKind.Claws,
            StartingScrollSchool: null,
            StartingScrollCount: 0,
            NarratorNote: """
                A CURSED SKINWALKER: they put on a beast's hide for a ritual and it never came off. It has
                grown into them -- claws where hands were, and something under the pelt that is awake.
                - Animals panic near them. Dogs will not stop screaming.
                - The curse mutters. Describe its appetite; it wants things the character does not.
                - People take them for a monster on sight, and are not entirely wrong.
                """),

        _ => throw new ArgumentOutOfRangeException(nameof(characterClass), characterClass, null)
    };
}
