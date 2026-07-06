using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Campaigns;

/// <summary>Maps each difficulty level to its settings. The single source of the difficulty numbers.</summary>
public static class DifficultyPresets
{
    public static DifficultySettings For(Difficulty level) => level switch
    {
        Difficulty.StoryMode => new DifficultySettings(
            StartingHpBonus: 8,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 2),
            DeadlyDamage: DiceExpr.D(1, 4),
            DawnDice: DiceExpr.D(1, 8),
            GmToneNote: "Difficulty: STORY MODE. Be forgiving — favor tension over death. Prefer None or Minor consequences; reserve Deadly for reckless, self-destructive acts."),
        Difficulty.Grim => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 4),
            DeadlyDamage: DiceExpr.D(1, 6),
            DawnDice: DiceExpr.D(1, 6),
            GmToneNote: "Difficulty: GRIM. Measured danger. Default to None or Minor; use Serious only for genuine peril; reserve Deadly for explicit death-traps."),
        Difficulty.Doomed => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 2),
            SeriousDamage: DiceExpr.D(1, 6),
            DeadlyDamage: DiceExpr.D(1, 10),
            DawnDice: DiceExpr.D(1, 6),
            GmToneNote: "Difficulty: DOOMED. True MORK BORG — unfair and grim. Let Serious and Deadly consequences fall as the fiction demands."),
        Difficulty.Hardcore => new DifficultySettings(
            StartingHpBonus: 0,
            MinorDamage: DiceExpr.D(1, 4),
            SeriousDamage: DiceExpr.D(1, 8),
            DeadlyDamage: DiceExpr.D(1, 12),
            DawnDice: DiceExpr.D(1, 4),
            GmToneNote: "Difficulty: HARDCORE. Merciless — the world wants them dead. Reach readily for Serious and Deadly consequences."),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
