namespace WretchedWhispers.Engine.GameTools.Models;

public record ChallengeOutcomeDto(
    bool IsSuccess, int Roll, int Modifier, int Total, int Dr, int DamageTaken, bool IsDead, int CurrentHp,
    // The injury the Broken table gave, in Game State's words ("lost eye"). The narrator has no other way
    // to learn which one it was in the turn it happens.
    string? NewInjury = null)
{
    // Broken: the damage dropped the character to 0, they survived the Broken table with an injury, and
    // came round with the few hit points CurrentHp shows. The narrator must read this as "grievously
    // wounded, alive" — never as death (the fabrication this closes).
    public bool IsBroken => NewInjury is not null;
}
