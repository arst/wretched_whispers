using WretchedWhispers.Core.Characters.Weapon;
using WretchedWhispers.Core.Dice;

namespace WretchedWhispers.Core.Combat.Attack;

public sealed class AttackRequest(Weapon weapon, Dr baseDr = default)
{
    public Weapon Weapon { get; } = weapon;
    public Dr BaseDr { get; } = baseDr.Value == 0 ? new Dr(12) : baseDr;
}