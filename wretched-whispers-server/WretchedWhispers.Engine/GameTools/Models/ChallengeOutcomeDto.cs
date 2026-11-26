using System.Text.Json.Serialization;
using WretchedWhispers.Core.Campaigns.Time;
using WretchedWhispers.Core.Characters.Status;

namespace WretchedWhispers.Engine.GameTools.Models;

public record ChallengeOutcomeDto(
    bool IsSuccess, int Roll, int Modifier, int Total, int Dr, int DamageTaken, bool IsDead, int CurrentHp,
    // The injury the Broken table gave, in Game State's words ("lost eye"). The narrator has no other way
    // to learn which one it was in the turn it happens.
    string? NewInjury = null,
    // Why Dr is not the difficulty asked for, each cause with its size ("injury +2, omen -4"); null
    // when nothing moved it.
    string? DrBecause = null,
    // What the clock reads after the hours the attempt took; null when it took a moment.
    TimePassedDto? TimePassed = null)
{
    // Broken: the damage dropped the character to 0, they survived the Broken table with an injury, and
    // came round with the few hit points CurrentHp shows. The narrator must read this as "grievously
    // wounded, alive" — never as death (the fabrication this closes).
    public bool IsBroken => NewInjury is not null;

    /// <summary>Dead or broken, in words, as the first thing in the result. See <see cref="FateWords"/>.</summary>
    [JsonPropertyOrder(-1)]
    public string? Fate => FateWords.For(IsDead, NewInjury, CurrentHp);

    /// <summary>What the roll means for the sheet, in words, beside fate. With only isSuccess, a failed test
    /// paid out the strongbox it was for (4 of 6 in an eval), and a passed haggle or theft was told as done
    /// with nothing bought or added (third and fourth simulated runs).</summary>
    [JsonPropertyOrder(-1)]
    public string Outcome => IsSuccess
        ? "PASSED. What was attempted happens, but nothing reaches the sheet unless you call the tool that "
            + "applies it: an item taken or stolen is AddItemToCharacterInventory, a purchase is BuyItem, coins are "
            + "GainSilver."
        : "FAILED. What was attempted does not happen: the lock holds, the lie is seen through, the coins stay "
            + "where they are. Grant nothing the attempt was for.";
}

/// <summary>The clock after a test that took hours, and what those hours brought. The rest of
/// <see cref="AdvanceTimeOutcome"/> describes a rest and would sit beside the test's own isDead and
/// currentHp saying something else.</summary>
public record TimePassedDto(int Day, int Hour, bool IsNewDawn, List<string> Miseries, bool IsWorldEnded)
{
    public static TimePassedDto From(AdvanceTimeOutcome time) =>
        new(time.Day, time.Hour, time.IsNewDawn, time.Miseries, time.IsWorldEnded);
}
