using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Status;

/// <summary>
///     Immutable value object representing a set of injuries from the Mork Borg broken table.
///     Uses a flags enum internally for O(1) bitwise operations and compact integer serialization.
/// </summary>
/// <remarks>
///     Penalty methods return deterministic DR increase values matching Character.Challenge logic.
///     Dice-based penalties (SmashedFace, LostEye) return DiceExpr for the caller to roll.
///     IsDead, IsInfected, and IsDizzyFromMagic are NOT part of InjurySet -- they are separate
///     conditions on Character with their own lifecycle.
/// </remarks>
public readonly record struct InjurySet(InjuryKind Injuries = InjuryKind.None)
{
    /// <summary>Checks whether the specified injury is present.</summary>
    public bool Has(InjuryKind injury) => injury != InjuryKind.None && (Injuries & injury) == injury;

    /// <summary>Returns a new InjurySet with the specified injury added (idempotent).</summary>
    public InjurySet Add(InjuryKind injury) => new(Injuries | injury);

    /// <summary>
    ///     Returns the fixed DR increase for Strength checks.
    ///     SeveredArm = +4, BrokenHand = +2. SeveredArm dominates (max, not sum).
    /// </summary>
    public int GetStrengthPenalty()
    {
        if (Has(InjuryKind.SeveredArm)) return 4;
        if (Has(InjuryKind.BrokenHand)) return 2;
        return 0;
    }

    /// <summary>
    ///     Returns the fixed DR increase for Agility checks.
    ///     StabbedLung or CrushedFoot = +2. Both present still gives +2 (same code branch in Character.Challenge).
    /// </summary>
    public int GetAgilityPenalty()
    {
        if (Has(InjuryKind.StabbedLung) || Has(InjuryKind.CrushedFoot)) return 2;
        return 0;
    }

    /// <summary>
    ///     Returns the dice expression for the Presence penalty.
    ///     SmashedFace = D4 (rolled by caller). No SmashedFace = Zero.
    /// </summary>
    public DiceExpr GetPresencePenaltyDice()
    {
        return Has(InjuryKind.SmashedFace) ? DiceExpr.D4 : DiceExpr.Zero;
    }

    /// <summary>
    ///     Returns the dice expression for the Agility penalty from LostEye.
    ///     LostEye = D4 (rolled by caller). No LostEye = Zero.
    ///     This is separate from the fixed Agility DR increase from StabbedLung/CrushedFoot.
    /// </summary>
    public DiceExpr GetAgilityPenaltyDice()
    {
        return Has(InjuryKind.LostEye) ? DiceExpr.D4 : DiceExpr.Zero;
    }
}
