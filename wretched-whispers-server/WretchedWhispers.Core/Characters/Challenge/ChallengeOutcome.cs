using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Challenge;

public class ChallengeOutcome
{
    private ChallengeOutcome(bool isSuccess, Natural natural, int roll, int modifier, int effectiveDr,
        IReadOnlyList<string> drReasons)
    {
        DrReasons = drReasons;
        IsSuccess = isSuccess;
        Natural = natural;
        Roll = roll;
        Modifier = modifier;
        EffectiveDr = effectiveDr;
    }

    public bool IsSuccess { get; }
    public Natural Natural { get; }
    /// <summary>Raw d20 roll.</summary>
    public int Roll { get; }
    /// <summary>Ability modifier added to the roll.</summary>
    public int Modifier { get; }
    /// <summary>DR after encumbrance/injury adjustments — what the total was compared against.</summary>
    public int EffectiveDr { get; }

    /// <summary>What moved the DR from the one asked for, each with its size: "armor +2", "omen -4".
    /// Empty when nothing did.</summary>
    public IReadOnlyList<string> DrReasons { get; }

    public static ChallengeOutcome Success(Natural natural, int roll, int modifier, int effectiveDr,
        IReadOnlyList<string> drReasons) =>
        new(true, natural, roll, modifier, effectiveDr, drReasons);

    public static ChallengeOutcome Fail(Natural natural, int roll, int modifier, int effectiveDr,
        IReadOnlyList<string> drReasons) =>
        new(false, natural, roll, modifier, effectiveDr, drReasons);
}
