using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Classes;

/// <summary>The concrete knobs a class resolves to. Pure value object, never persisted.
/// <para>
/// Ability bonuses are deltas applied to the 3d6 ROLL, before the roll is mapped to a modifier -- that
/// is what the rules mean by "Strength +2", and it is why they cannot push a score out of range: the
/// mapping caps at -3..+3 whatever the sum. Adding them to the mapped modifier instead would make a
/// class bonus roughly three times stronger than intended.
/// </para>
/// <para>
/// There is deliberately no power die: every class rolls Presence + d4 uses per day, so the knob would
/// only ever hold one value.
/// </para>
/// <para>
/// <see cref="NarratorNote"/> is the flavour half of the feature: prose handed to the narrator for the
/// abilities that are judgement rather than arithmetic. Same trick as
/// <see cref="Campaigns.DifficultySettings.GmToneNote"/>.
/// </para></summary>
public sealed record ClassSettings(
    string DisplayName,
    int StrengthBonus,
    int AgilityBonus,
    int PresenceBonus,
    int ToughnessBonus,
    DiceExpr HpDie,
    DiceExpr OmenDie,
    // Which slice of the starting-gear tables the class may roll on: a smaller die is a worse kit.
    // Beginning with a scroll caps these at d6/d2 on top, the trade the classless rules already make.
    DiceExpr WeaponDie,
    DiceExpr ArmorDie,
    DiceExpr SilverDice,
    // False means illiterate: scrolls rolled on the gear tables land in the pack as unreadable paper.
    bool CanUseScrolls,
    // Set means this replaces the rolled starting weapon outright.
    // ponytail: a natural attack can't be a *choice* until weapons are swappable -- Character.Weapon is
    // written once at creation and only ever downgraded to Improvised on breakage.
    WeaponKind? NaturalWeapon,
    // Null school with a non-zero count means "roll sacred or unclean per scroll".
    ScrollSchool? StartingScrollSchool,
    int StartingScrollCount,
    string NarratorNote);
