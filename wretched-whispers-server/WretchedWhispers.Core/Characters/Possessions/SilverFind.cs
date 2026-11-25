namespace WretchedWhispers.Core.Characters.Possessions;

/// <summary>How big a find of silver is. The narrator names the size and where it came from; the dice
/// say how much, so neither a player who declares a sum nor a narrator who gives in can set it.</summary>
public enum SilverFind
{
    /// <summary>A few loose coins: a pocket, a beggar's bowl, under a jar. d6.</summary>
    Coins,

    /// <summary>A purse on a belt, a small payment. 2d10.</summary>
    Purse,

    /// <summary>A hidden cache, a strongbox, a job's full wages. 4d10.</summary>
    Cache,
}
