using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Inventory.Armor;

namespace WretchedWhispers.Core.Adversaries;

public class Adversary(string name, HitPoints hp, Armor armor, int morale, AttackProfile attack)
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; } = name;

    public HitPoints Hp { get; private set; } = hp;

    public Armor Armor { get; private set; } = armor;

    public int Morale { get; } = morale;

    public AttackProfile Attack { get; } = attack;

    public bool IsDead => Hp.Current <= 0;

    public bool IsFled { get; private set; }

    public void ReceiveDamage(int amount)
    {
        Hp = Hp.Damage(amount);
    }

    public void Retreat()
    {
        IsFled = true;
    }
}