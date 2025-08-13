using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Characters.Armor.Tiers;
using WretchedWhispers.Core.Characters.Weapon;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Outcomes;
using WretchedWhispers.Core.Powers;
using WretchedWhispers.Core.Scrolls;
using WretchedWhispers.Core.Test;

namespace WretchedWhispers.Core.Characters;

public sealed class Character
{
    private readonly List<Scroll> _knownScrolls;

    private Character(Guid id, string name, Abilities.Abilities abilities, int silver, int foodDays, Gear gear,
        Weapon.Weapon weapon,
        Armor.Armor armor,
        Shield? shield, List<Scroll> scrolls, int currentHp, int maxHp, int omenCount = 0)
    {
        Id = id;
        Name = name;
        Abilities = abilities;
        Silver = silver;
        FoodDays = foodDays;
        Gear = gear;
        Weapon = weapon;
        Armor = armor;
        Shield = shield;
        Omens = new Omens(omenCount);
        Hp = new HitPoints(currentHp, maxHp);
        _knownScrolls = scrolls;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Abilities.Abilities Abilities { get; }
    public int Silver { get; }
    public int FoodDays { get; }
    public Gear Gear { get; }
    public HitPoints Hp { get; private set; }
    public Armor.Armor Armor { get; }
    public Shield? Shield { get; }
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

    public AttackOutcome Attack(IRandomService rng, Armor.Armor targetArmor)
    {
        var outcome = Combat.Combat.ResolvePlayerAttack(new Dice.Dice(rng), Abilities, Weapon, targetArmor);

        if (outcome.Fumble)
            // Weapon breaks => fallback to Improvised
            Weapon = Characters.Weapon.Weapon.Create(WeaponKind.Improvised);

        if (outcome.TargetArmorDegraded) targetArmor.Degrade();

        return new AttackOutcome(outcome.Hit, outcome.Damage, outcome.Critical, outcome.Fumble, outcome.WeaponBroken,
            outcome.TargetArmorDegraded);
    }

    public DefenceOutcome Defend(IRandomService rng, DiceExpr attackDie)
    {
        var outcome = Combat.Combat.ResolvePlayerDefence(new Dice.Dice(rng), Abilities, new DefenceRequest(), Armor);

        if (outcome.Avoided)
            return new DefenceOutcome();

        var damage = rng.Roll(attackDie);

        if (outcome.FumbleDoubleDamage) damage *= 2; // Fumble doubles the damage

        if (outcome.CriticalFreeAttack)
        {
            var freeAttackResults = Defend(rng, attackDie); // Crit grants a free attack
            damage += freeAttackResults.DamageDealt;
        }

        var armorReduction =
            RollArmorReduction(rng, Armor) +
            (Shield is not null
                ? 1
                : 0); // Shild adds +1 to armor reduction or completely blocks one attack and breaks, model as +1 to armor reduction fo now

        damage -= armorReduction;

        // TODO: Implement armor tier degradation

        return new DefenceOutcome
        {
            DamageDealt = damage
        };
    }

    private static int RollArmorReduction(IRandomService rng, Armor.Armor armor)
    {
        return armor.Tier switch
        {
            HeavyArmorTier => rng.D(6),
            LightArmorTier => rng.D(4),
            MediumArmorTier => rng.D(3),
            NoArmorTier => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(armor.Tier), armor.Tier, null)
        };
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

        var test = Test.Test.Roll(new Dice.Dice(rng), Abilities.Presence, 12);
        if (test.Outcome == TestOutcome.Success) return CastOutcome.Success(scroll.Key);

        var loss = rng.D(2);
        Hp = Hp.Damage(loss);
        IsDizzyFromMagic = true;
        return CastOutcome.Fizzle(scroll.Key, loss);
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

    public static Character Create(Guid id, string name, int maxHp, Abilities.Abilities abilities,
        StartingEquipment equipment, int startingOmensCount = 0)
    {
        return new Character(
            id,
            name,
            abilities,
            equipment.Silver,
            equipment.FoodDays,
            new Gear(equipment.Container, equipment.Gear1, equipment.Gear2),
            equipment.Weapon,
            equipment.Armor,
            equipment.Shield,
            equipment.Scrolls,
            maxHp,
            maxHp,
            startingOmensCount);
    }
}