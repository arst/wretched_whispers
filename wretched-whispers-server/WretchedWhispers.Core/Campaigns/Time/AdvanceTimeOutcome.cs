namespace WretchedWhispers.Core.Campaigns.Time;

/// <summary>What passing time did. The last five say what a rest did to the character, in the words the
/// other results use: a rest heals, or, taken while infected, hurts and can break or kill.</summary>
public record AdvanceTimeOutcome(
    List<string> Miseries, bool IsWorldEnded, bool IsNewDawn, int OmensRefreshed = 0,
    int HpHealed = 0, int DamageTaken = 0, int? CurrentHp = null, string? NewInjury = null, bool IsDead = false);
