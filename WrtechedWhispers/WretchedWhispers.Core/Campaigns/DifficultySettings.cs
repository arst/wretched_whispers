using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>The concrete knobs a difficulty level resolves to. Pure value object.</summary>
public sealed record DifficultySettings(
    int StartingHpBonus,
    DiceExpr MinorDamage,
    DiceExpr SeriousDamage,
    DiceExpr DeadlyDamage,
    DiceExpr DawnDice,
    string GmToneNote);
