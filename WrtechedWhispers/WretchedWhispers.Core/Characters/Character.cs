using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Characters.Inventory;
using WretchedWhispers.Core.Characters.Inventory.Armor;
using WretchedWhispers.Core.Characters.Inventory.Armor.Tiers;
using WretchedWhispers.Core.Characters.Inventory.Weapon;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Outcomes;
using WretchedWhispers.Core.Powers;
using WretchedWhispers.Core.Scrolls;

namespace WretchedWhispers.Core.Characters;

public sealed class Character
{
    private readonly List<Scroll> _knownScrolls;

    private Character(Guid id, string name, Abilities.Abilities abilities, int silver, int foodDays, Gear gear,
        Weapon weapon,
        Armor armor,
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
    public Armor Armor { get; }
    public Shield? Shield { get; }
    public Weapon Weapon { get; private set; }
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

    public void NewDawn()
    {
        Powers.ResetForNewDay(Abilities.Presence);
        IsDizzyFromMagic = false;
    }

    public AttackOutcome Attack(Armor targetArmor)
    {
        var outcome = ResolveAttack(targetArmor);

        if (outcome.Fumble)
            // Weapon breaks => fallback to Improvised
            Weapon = Weapon.Create(WeaponKind.Improvised);

        if (outcome.TargetArmorDegraded) targetArmor.Degrade();

        return new AttackOutcome(outcome.Hit, outcome.Damage, outcome.Critical, outcome.Fumble, outcome.WeaponBroken,
            outcome.TargetArmorDegraded);
    }

    private AttackOutcome ResolveAttack(Armor targetArmor)
    {
        var ability = Weapon.IsRanged ? Abilities.Presence : Abilities.Strength;
        var test = ability.Test(new Dr(12));

        var hit = test.Outcome == TestOutcome.Success;
        var crit = test.IsCrit;
        var fumble = test.IsFumble;
        var weaponBroken = false;
        var targetArmorDegraded = false;

        var dmg = Damage.Zero;
        if (hit)
        {
            var raw = Dice.Roll(Weapon.DamageDie);
            if (crit) raw *= 2;
            // Armor damage reduction
            var reduction = targetArmor.DamageReduction.Sides == 0 ? 0 : Dice.Roll(targetArmor.DamageReduction);
            var final = Math.Max(0, raw - reduction);
            dmg = Damage.From(final);

            if (crit && targetArmor.Tier is not NoArmorTier) targetArmorDegraded = true;
        }
        else if (fumble)
        {
            // Weapon breaks or is lost, model as broken for now
            weaponBroken = true;
        }

        return new AttackOutcome(hit, dmg, crit, fumble, weaponBroken, targetArmorDegraded);
    }

    public DefenceOutcome Defend(DiceExpr attackDie)
    {
        var outcome = ResolveDefence();

        if (outcome.IsAvoided)
            return new DefenceOutcome
            {
                DamageDealt = 0,
                Avoided = outcome.IsAvoided,
                CriticalFreeAttack = outcome.IsCritFree,
                FumbleDoubleDamage = outcome.IsFumble
            };

        var damage = Dice.Roll(attackDie);

        if (outcome.IsFumble) damage *= 2; // Fumble doubles the damage

        if (outcome.IsCritFree)
        {
            var freeAttackResults = Defend(attackDie); // Crit grants a free attack
            damage += freeAttackResults.DamageDealt;
        }

        var armorReduction =
            RollArmorReduction(Armor) +
            (Shield is not null
                ? 1
                : 0); // Shield adds +1 to armor reduction or completely blocks one attack and breaks, model as +1 to armor reduction fo now

        damage -= armorReduction;

        // TODO: Implement armor tier degradation + shield break

        return new DefenceOutcome
        {
            DamageDealt = damage,
            Avoided = outcome.IsAvoided,
            CriticalFreeAttack = outcome.IsCritFree,
            FumbleDoubleDamage = outcome.IsFumble
        };
    }

    private (bool IsAvoided, bool IsCritFree, bool IsFumble) ResolveDefence()
    {
        var dr = new Dr(new Dr(12).Value + Armor.DefencePenalty);
        var abilityScore = new AbilityScore(Abilities.Agility.Modifier - Armor.AgilityPenalty);
        var test = abilityScore.Test(dr);
        var avoided = test.Outcome == TestOutcome.Success;
        var critFree = test.IsCrit; // free attack granted to the attacker
        var fumble = test.IsFumble;

        return (avoided, critFree, fumble);
    }

    private static int RollArmorReduction(Armor armor)
    {
        return armor.Tier switch
        {
            HeavyArmorTier => Dice.Roll(DiceExpr.D6),
            LightArmorTier => Dice.Roll(DiceExpr.D4),
            MediumArmorTier => Dice.Roll(DiceExpr.D3),
            NoArmorTier => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(armor.Tier), armor.Tier, null)
        };
    }

    public BrokenOutcome? ResolveBroken()
    {
        if (!Hp.IsZero) return null;
        var d4 = Dice.Roll(DiceExpr.D4);
        return d4 switch
        {
            1 => BrokenOutcome.Unconscious(Dice.Roll(DiceExpr.D4), Dice.Roll(DiceExpr.D4)),
            2 => Dice.Roll(DiceExpr.D6) == 6
                ? BrokenOutcome.LostEye(Dice.Roll(DiceExpr.D4))
                : BrokenOutcome.BrokenOrSeveredLimb(Dice.Roll(DiceExpr.D4)),
            3 => BrokenOutcome.Hemorrhage(),
            _ => BrokenOutcome.Dead()
        };
    }

    public void Rest(bool isFullNightRest)
    {
        if (IsInfected)
        {
            Hp = Hp.Damage(Dice.Roll(DiceExpr.D6));
            return;
        }

        var heal = isFullNightRest ? Dice.Roll(DiceExpr.D6) : Dice.Roll(DiceExpr.D4);
        Hp = Hp.Heal(heal);
    }

    public CastOutcome Cast(Scroll scroll)
    {
        if (IsDizzyFromMagic)
            return CastOutcome.Fail("Dizzy from prior failure");
        if (ScrollRestrictionPolicy.CanUseScrolls(Weapon, Armor))
            return CastOutcome.Fail("Can't use scrolls, because armor is too heavy or weapon is two-handed");
        if (!Powers.TryConsumeOne())
            return CastOutcome.Fail("No daily power uses remaining");

        var test = Abilities.Presence.Test(12);
        if (test.Outcome == TestOutcome.Success) return CastOutcome.Success(scroll.Key);

        var loss = Dice.Roll(DiceExpr.D2);
        Hp = Hp.Damage(loss);
        IsDizzyFromMagic = true;
        return CastOutcome.Fizzle(scroll.Key, loss);
    }

    public ChallengeOutcome Challenge(Dr challenge, AbilityKind ability)
    {
        var rollResults = Dice.Roll(DiceExpr.D20);
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