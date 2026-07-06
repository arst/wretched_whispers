namespace WretchedWhispers.Core.Characters.Challenge;

/// <summary>GM ruling for what failing a challenge costs. The model picks the category; the domain rolls the numbers.</summary>
public enum ChallengeConsequence
{
    None,
    /// <summary>d2 damage — scrapes and bruises.</summary>
    Minor,
    /// <summary>d6 damage — a real wound.</summary>
    Serious,
    /// <summary>d10 damage — can kill outright.</summary>
    Deadly
}
