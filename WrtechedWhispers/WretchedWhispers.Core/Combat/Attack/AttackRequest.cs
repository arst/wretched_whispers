using WretchedWhispers.Core.Character.Weapon;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Combat.Attack;

public sealed class AttackRequest
{
    public AttackRequest(AttackKind kind, Weapon weapon, Dr baseDr = default)
    {
        Kind = kind;
        Weapon = weapon;
        BaseDr = baseDr.Value == 0 ? new Dr(12) : baseDr; // default DR12
    }

    public AttackKind Kind { get; }
    public Weapon Weapon { get; }
    public Dr BaseDr { get; }
}