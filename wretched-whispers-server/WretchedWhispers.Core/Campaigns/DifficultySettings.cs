using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>The concrete knobs a difficulty level resolves to. Pure value object.</summary>
public sealed record DifficultySettings(
    int StartingHpBonus,
    DiceExpr MinorDamage,
    DiceExpr SeriousDamage,
    DiceExpr DeadlyDamage,
    DiceExpr DawnDice,
    string GmToneNote,
    // MORK BORG "Getting Better": whether a low ability roll (d6 < score) degrades the ability.
    // RAW says yes; StoryMode is improvement-only so forgiving campaigns don't regress.
    bool AbilityLossOnGettingBetter);
