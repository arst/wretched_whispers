using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Classes;

/// <summary>The concrete knobs a class resolves to. Pure value object, never persisted.
/// <para>
/// Ability bonuses are deltas applied to the rolled 3d6 modifier and MUST be clamped by the caller --
/// <see cref="Abilities.AbilityScore"/> throws outside -3..+6, and a rolled -3 plus a negative class
/// bonus is reachable.
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
    DiceExpr PowerDie,
    // Set means this replaces the rolled starting weapon outright.
    // ponytail: a natural attack can't be a *choice* until weapons are swappable -- Character.Weapon is
    // written once at creation and only ever downgraded to Improvised on breakage.
    WeaponKind? NaturalWeapon,
    ScrollSchool? StartingScrollSchool,
    int StartingScrollCount,
    string NarratorNote);
