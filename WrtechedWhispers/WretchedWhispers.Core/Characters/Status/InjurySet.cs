using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters.Status;

public readonly record struct InjurySet(InjuryKind Injuries = InjuryKind.None)
{
    public bool Has(InjuryKind injury) => throw new NotImplementedException();
    public InjurySet Add(InjuryKind injury) => throw new NotImplementedException();
    public int GetStrengthPenalty() => throw new NotImplementedException();
    public int GetAgilityPenalty() => throw new NotImplementedException();
    public DiceExpr GetPresencePenaltyDice() => throw new NotImplementedException();
    public DiceExpr GetAgilityPenaltyDice() => throw new NotImplementedException();
    public int GetToughnessPenalty() => throw new NotImplementedException();
}
