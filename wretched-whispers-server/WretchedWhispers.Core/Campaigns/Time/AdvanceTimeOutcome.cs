namespace WretchedWhispers.Core.Campaigns.Time;

/// <summary>What passing time did. The five after OmensRefreshed say what a rest did to the character,
/// in the words the other results use: a rest heals, or, taken while infected, hurts and can break or kill.
/// Day and Hour are what the clock reads afterwards, so the hour told is the hour on the sheet.</summary>
public record AdvanceTimeOutcome(
    List<string> Miseries, bool IsWorldEnded, bool IsNewDawn, int OmensRefreshed = 0,
    int HpHealed = 0, int DamageTaken = 0, int? CurrentHp = null, string? NewInjury = null, bool IsDead = false,
    int Day = 0, int Hour = 0);
