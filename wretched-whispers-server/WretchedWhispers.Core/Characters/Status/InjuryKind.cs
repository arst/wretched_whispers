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
