namespace WretchedWhispers.Core.Characters.Status;

/// <summary>
/// What a result that killed or broke the character says, in words, ahead of its numbers. The flags
/// were not enough: a result with isDead true beside currentHp 0 and isBroken false was told as "broken,
/// not dead" in 4 of the 8 deaths from a failed test in the third simulated run, each from a cause too
/// small for the narrator to believe (a haggle, a sneak).
/// </summary>
public static class FateWords
{
    /// <summary>Null when the character was neither killed nor broken.</summary>
    public static string? For(bool isDead, string? newInjury, int? currentHp) =>
        isDead
            ? "DEAD. This killed the character, however small the cause. It is final: tell the death now. "
              + "They are not broken, not unconscious, and cannot act again."
            : newInjury is not null
                ? $"BROKEN, NOT DEAD. Struck down to 0 hit points and alive, with this injury: {newInjury}. "
                  + $"They come round at {currentHp} hit points."
                : null;
}
