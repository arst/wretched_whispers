using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Character.Armor;
using WretchedWhispers.Core.Character.Armor.Tiers;
using WretchedWhispers.Core.Character.Weapon;
using WretchedWhispers.Core.Combat;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Outcomes;
using WretchedWhispers.Core.Powers;
using WretchedWhispers.Core.Scrolls;
using WretchedWhispers.Core.Test;

namespace WretchedWhispers.Core.Character;

public sealed class Character
{
    private readonly List<Scroll> _knownScrolls = [];

    private Character(string name, Abilities.Abilities abilities, Weapon.Weapon weapon, Armor.Armor armor,
        Shield? shield, int currentHp, int maxHp, int omenCount = 0)
    {
        Name = name;
        Abilities = abilities;
        Weapon = weapon;
        Armor = armor;
        Shield = shield;
        Omens = new Omens(omenCount);
        Hp = new HitPoints(currentHp, maxHp);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; private set; }
    public Abilities.Abilities Abilities { get; }
    public HitPoints Hp { get; private set; }
    public Armor.Armor Armor { get; }
    public Shield? Shield { get; private set; }
    public Weapon.Weapon Weapon { get; private set; }
    public Omens Omens { get; private set; }
    public PowerPool Powers { get; } = new();

    public bool IsInfected { get; private set; }
    public bool IsDizzyFromMagic { get; private set; }
    
    public IReadOnlyCollection<Scroll> KnownScrolls => _knownScrolls;

    public void LearnScroll(Scroll s)
    {
        _knownScrolls.Add(s);
    }

    public void Infect()
    {
        IsInfected = true;
    }

    public void CureInfection()
    {
        IsInfected = false;
    }

    public void NewDawn(Dice.Dice dice)
    {
        Powers.ResetForNewDay(dice, Abilities.Presence);
        IsDizzyFromMagic = false;
    }

    public AttackOutcome Attack(IRandomService rng, AttackKind kind, Armor.Armor targetArmor)
    {
        var outcome = Combat.Combat.ResolvePlayerAttack(rng, Abilities, new AttackRequest(kind, Weapon), targetArmor);
        if (outcome.Fumble)
            // Weapon breaks => fallback to Improvised
            Weapon = Core.Character.Weapon.Weapon.Create(WeaponKind.Improvised);
        return outcome;
    }

    public DefenceOutcome Defend(IRandomService rng)
    {
        return Combat.Combat.ResolvePlayerDefence(rng, Abilities, new DefenceRequest(), Armor);
    }

    public void ReceiveDamage(IRandomService rng, Damage incoming, bool doubleOnDefenceFumble)
    {
        var amount = incoming.Amount;
        if (doubleOnDefenceFumble) amount *= 2;
        Hp = Hp.Damage(amount);
    }

    public BrokenOutcome? ResolveBroken(IRandomService rng)
    {
        if (!Hp.IsZero) return null;
        var d4 = rng.D(4);
        return d4 switch
        {
            1 => BrokenOutcome.Unconscious(rng.D(4), rng.D(4)),
            2 => rng.D(6) == 6 ? BrokenOutcome.LostEye(rng.D(4)) : BrokenOutcome.BrokenOrSeveredLimb(rng.D(4)),
            3 => BrokenOutcome.Hemorrhage(),
            4 => BrokenOutcome.Dead(),
            _ => BrokenOutcome.Dead()
        };
    }

    public void Rest(IRandomService rng, bool isFullNightRest)
    {
        if (IsInfected)
        {
            Hp = Hp.Damage(rng.D(6));
            return;
        }

        var heal = isFullNightRest ? rng.D(6) : rng.D(4);
        Hp = Hp.Heal(heal);
    }

    public CastOutcome Cast(IRandomService rng, Scroll scroll)
    {
        if (IsDizzyFromMagic)
            return CastOutcome.Fail("Dizzy from prior failure");
        if (ScrollRestrictionPolicy.CanUseScrolls(Weapon, Armor))
            return CastOutcome.Fail("Can't use scrolls, because armor is too heavy or weapon is two-handed");
        if (!Powers.TryConsumeOne())
            return CastOutcome.Fail("No daily power uses remaining");

        var test = Test.Test.Roll(rng, Abilities.Presence, 12);
        if (test.Outcome == TestOutcome.Success)
        {
            return CastOutcome.Success(scroll.Key);
        }
        
        var loss = rng.D(2);
        Hp = Hp.Damage(loss);
        IsDizzyFromMagic = true;
        return CastOutcome.Fizzle(scroll.Key, loss);
    }
    
    public Character Create(string name, Abilities.Abilities abilities, Weapon.Weapon weapon, Armor.Armor armor,
        Shield? shield, IRandomService rng, int startingOmensCount = 0)
    {
        var maxHp = Math.Max(1, abilities.Toughness.Modifier + rng.D(8));
        return new Character(name, Abilities, weapon, armor, shield, maxHp, maxHp, startingOmensCount);
    }

    public ChallengeOutcome Challenge(Dice.Dice dice, Dr challenge, AbilityKind ability)
    {
        var rollResults = dice.Roll(DiceExpr.d20);
        switch (rollResults)
        {
            case 1:
                return ChallengeOutcome.Fail();
            case 20:
                return ChallengeOutcome.Success();
            default:
            {
                var total = rollResults + Abilities[ability].Modifier;
                return total >= challenge.Value
                    ? ChallengeOutcome.Success()
                    : ChallengeOutcome.Fail();
            }
        }
    }
}