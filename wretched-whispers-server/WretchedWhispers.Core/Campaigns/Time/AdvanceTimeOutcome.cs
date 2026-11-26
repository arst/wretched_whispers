namespace WretchedWhispers.Core.Campaigns.Time;

/// <summary>What passing time did. The five after OmensRefreshed say what a rest did to the character,
/// in the words the other results use: a rest heals, or, taken while infected, hurts and can break or kill.
/// Day and Hour are what the clock reads afterwards, so the hour told is the hour on the sheet.</summary>
public record AdvanceTimeOutcome(
    List<string> Miseries, bool IsWorldEnded, bool IsNewDawn, int OmensRefreshed = 0,
    int HpHealed = 0, int DamageTaken = 0, int? CurrentHp = null, string? NewInjury = null, bool IsDead = false,
    int Day = 0, int Hour = 0)
{
    /// <summary>Dead or broken by a rest taken while infected, in words, as the first thing in the
    /// result. See <see cref="FateWords"/>.</summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string? Fate => WretchedWhispers.Core.Characters.Status.FateWords.For(IsDead, NewInjury, CurrentHp);

    /// <summary>Hours passing spend nothing the character carries. Evals of a night's camp: 2 to 3 of 12
    /// told the pack's torches guttering through the night with no item used, and a stage-prompt line
    /// naming torches made it 5 of 12. Said in the result, beside the hours, instead.</summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public string Carried => "Nothing the character carries was used up in these hours: every item in the pack "
        + "is as Game State shows it.";
}
