namespace WretchedWhispers.Core.Encounters;

public enum InitialReaction
{
    Kill,
    Angered,
    Indifferent,
    AlmostFriendly,
    Helpful
}

public static class InitialReactionExtensions
{
    /// <summary>The reaction in words, as Game State and tool results give it. As a bare enum name,
    /// "AlmostFriendly" turned up in the story (sixth simulated run).</summary>
    public static string DisplayName(this InitialReaction reaction) => reaction switch
    {
        InitialReaction.Kill => "out to kill",
        InitialReaction.Angered => "angered",
        InitialReaction.Indifferent => "indifferent",
        InitialReaction.AlmostFriendly => "almost friendly",
        InitialReaction.Helpful => "helpful",
        _ => reaction.ToString()
    };
}
