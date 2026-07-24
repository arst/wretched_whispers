using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;

namespace WretchedWhispers.Core.Adversaries;

public sealed class Adversary
{
    [JsonConstructor]
    public Adversary(Guid id, string name, HitPoints hp, Armor armor, int morale, AttackProfile attack, bool isFled = false)
    {
        Id = id;
        Name = name;
        Hp = hp;
        Armor = armor;
        Morale = morale;
        Attack = attack;
        IsFled = isFled;
    }

    public Adversary(string name, HitPoints hp, Armor armor, int morale, AttackProfile attack)
        : this(Guid.NewGuid(), name, hp, armor, morale, attack)
    {
    }

    public Guid Id { get; }

    public string Name { get; }

    [JsonInclude] public HitPoints Hp { get; private set; }

    [JsonInclude] public Armor Armor { get; private set; }

    public int Morale { get; }

    public AttackProfile Attack { get; }

    [JsonIgnore] public bool IsDead => Hp.Current <= 0;

    [JsonInclude] public bool IsFled { get; private set; }

    public void ReceiveDamage(int amount)
    {
        Hp = Hp.Damage(amount);
    }

    public void Retreat()
    {
        IsFled = true;
    }
}