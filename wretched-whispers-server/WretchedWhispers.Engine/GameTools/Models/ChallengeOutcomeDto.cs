namespace WretchedWhispers.Engine.GameTools.Models;

public record ChallengeOutcomeDto(
    bool IsSuccess, int Roll, int Modifier, int Total, int Dr, int DamageTaken, bool IsDead, int CurrentHp)
{
    // 0 HP but not dead = Broken: the character survived the Broken table with an injury. The narrator
    // must read this as "grievously wounded, alive" — never as death (the fabrication this closes).
    public bool IsBroken => CurrentHp <= 0 && !IsDead;
}