namespace WretchedWhispers.Core.Characters.Status;

[Flags]
public enum InjuryKind
{
    None = 0,
    LostEye = 1 << 0,
    StabbedLung = 1 << 1,
    BrokenHand = 1 << 2,
    CrushedFoot = 1 << 3,
    SeveredArm = 1 << 4,
    SmashedFace = 1 << 5,
}

public static class InjuryKindExtensions
{
    /// <summary>The one name for an injury: what Game State lists it as and what a result calls it.</summary>
    public static string DisplayName(this InjuryKind injury) => injury switch
    {
        InjuryKind.LostEye => "lost eye",
        InjuryKind.StabbedLung => "stabbed lung",
        InjuryKind.BrokenHand => "broken hand",
        InjuryKind.CrushedFoot => "crushed foot",
        InjuryKind.SeveredArm => "severed arm",
        InjuryKind.SmashedFace => "smashed face",
        _ => throw new ArgumentOutOfRangeException(nameof(injury), injury, "Not a single injury.")
    };
}
