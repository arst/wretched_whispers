using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Cast;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Characters.Powers;
using WretchedWhispers.Core.Characters.Status.Broken;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public sealed class Character
{
    private readonly List<Scroll> _scrolls;

    private Character(
        Guid id,
        string name,
        Abilities.Abilities abilities,
        int silver,
        int foodDays,
        Inventory inventory,
        Weapon weapon,
        Armor armor,
        Shield? shield,
        List<Scroll> scrolls,
        PowerPool powers,
        int currentHp,
        int maxHp,
        int omenCount = 0)
    {
        Id = id;
        Name = name;
        Abilities = abilities;
        Silver = silver;
        FoodDays = foodDays;
        Weapon = weapon;
        Armor = armor;
        Shield = shield;
        Powers = powers;
        Omens = new Omens(omenCount);
        Hp = new HitPoints(currentHp, maxHp);
        _scrolls = scrolls;
        Inventory = inventory;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Abilities.Abilities Abilities { get; }
    public int Silver { get; private set; }
    public int FoodDays { get; }

    public Inventory Inventory { get; private set; }

    public HitPoints Hp { get; private set; }
    public Armor Armor { get; }
    public Shield? Shield { get; }
    public Weapon Weapon { get; private set; }
    public Omens Omens { get; private set; }
    public PowerPool Powers { get; }

    public bool IsInfected { get; private set; }
    public bool IsDizzyFromMagic { get; private set; }
    public bool IsEncumbered => Inventory.IsEncumbered(Abilities.Strength);

    public bool IsDead { get; private set; }

    public bool HasLostEye { get; private set; }

    public bool HasStabbedLung { get; private set; }

    public bool HasBrokenHand { get; private set; }

    public bool HasCrushedFoot { get; private set; }

    public bool HasSeveredArm { get; private set; }

    public bool HasSmashedFace { get; private set; }

    public IReadOnlyCollection<Scroll> Scrolls => _scrolls;

    public void Infect()
    {
        IsInfected = true;
    }

    public void CureInfection()
    {
        IsInfected = false;
    }

    public void StartNewDay()
    {
        Powers.ResetForNewDay(Abilities);
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
        var abilityKind = Weapon.IsRanged ? AbilityKind.Presence : AbilityKind.Strength;
        var test = Challenge(new Dr(12), abilityKind);
        var hit = test.IsSuccess;
        var crit = test.Natural == Natural.Twenty;
        var fumble = test.Natural == Natural.One;
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

        var damage = CalculateDamageAfterDefense(attackDie, outcome);
        ReceiveDamage(damage);

        // TODO: Implement armor tier degradation + shield break

        return new DefenceOutcome
        {
            DamageDealt = damage,
            Avoided = outcome.IsAvoided,
            CriticalFreeAttack = outcome.IsCritFree,
            FumbleDoubleDamage = outcome.IsFumble
        };
    }

    private void ReceiveDamage(int damage)
    {
        Hp = Hp.Damage(damage);

        if (Hp.IsZero)
        {
            var brokenOutcome = ResolveBroken();
            switch (brokenOutcome)
            {
                case null:
                    break;
                case BrokenHand:
                    HasBrokenHand = true;
                    break;
                case CrushedFoot:
                    HasCrushedFoot = true;
                    break;
                case DeadBroken:
                    IsDead = true;
                    break;
                case EyeLost:
                    HasLostEye = true;
                    break;
                case SeveredArm:
                    HasSeveredArm = true;
                    break;
                case SmashedFace:
                    HasSmashedFace = true;
                    break;
                case StabbedLung:
                    HasStabbedLung = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(brokenOutcome));
            }
        }
    }


    private int CalculateDamageAfterDefense(DiceExpr attackDie,
        (bool IsAvoided, bool IsCritFree, bool IsFumble) outcome)
    {
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
        return damage;
    }

    private (bool IsAvoided, bool IsCritFree, bool IsFumble) ResolveDefence()
    {
        var dr = new Dr(new Dr(12).Value + Armor.DefencePenalty);
        var test = Challenge(dr, AbilityKind.Agility, Armor.AgilityPenalty);
        var avoided = test.IsSuccess;
        var critFree = test.Natural == Natural.Twenty;
        var fumble = test.Natural == Natural.One;

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

    private BrokenOutcome? ResolveBroken()
    {
        if (!Hp.IsZero)
            return null;

        var d4 = Dice.Roll(DiceExpr.D4);

        if (d4 is 1 or 2) return BrokenOutcome.Dead();

        if (d4 > 4) return null;

        var d6 = Dice.Roll(DiceExpr.D6);

        return d6 switch
        {
            1 => BrokenOutcome.SeveredArm(),
            2 => BrokenOutcome.CrushedFoot(),
            3 => BrokenOutcome.SmashedFace(),
            4 => BrokenOutcome.StabbedLung(),
            5 => BrokenOutcome.BrokenHand(),
            6 => BrokenOutcome.EyeLost(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public void Rest(int hours)
    {
        if (IsInfected)
        {
            ReceiveDamage(Dice.Roll(DiceExpr.D6));
            return;
        }

        var isFullNightRest = hours >= 8;
        var heal = isFullNightRest ? Dice.Roll(DiceExpr.D6) : Dice.Roll(DiceExpr.D4);
        Hp = Hp.Heal(heal);
    }

    public void BuyItem(int price, InventoryItem item)
    {
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

        if (Inventory.IsFull)
            throw new InvalidOperationException("Inventory is full, throw away another item to add a new one.");

        if (Silver < price) throw new InvalidOperationException("Not enough silver to buy the item.");

        Inventory = Inventory with { InventoryItems = Inventory.InventoryItems.Append(item).ToList() };
        Silver -= price;
    }

    public CastOutcome Cast(Guid scrollId)
    {
        var scroll = _scrolls.FirstOrDefault(s => s.Id == scrollId);

        if (scroll is null) throw new InvalidOperationException("Scroll not found in inventory.");

        if (IsDizzyFromMagic)
            return CastOutcome.Fail("Dizzy from prior failure");
        if (ScrollRestrictionPolicy.CanUseScrolls(Weapon, Armor))
            return CastOutcome.Fail("Can't use scrolls, because armor is too heavy or weapon is two-handed");
        if (!Powers.TryConsumeOne())
            return CastOutcome.Fail("No daily power uses remaining");

        var challengeOutcome = Challenge(new Dr(12), AbilityKind.Presence);

        if (challengeOutcome.IsSuccess) return CastOutcome.Success(scroll.Description);

        var loss = Dice.Roll(DiceExpr.D2);
        ReceiveDamage(loss);
        IsDizzyFromMagic = true;
        return CastOutcome.Fizzle(scroll.Description, loss);
    }

    public ChallengeOutcome Challenge(Dr challenge, AbilityKind ability, int penalty = 0)
    {
        switch (ability)
        {
            case AbilityKind.Strength or AbilityKind.Agility when IsEncumbered:
                challenge = new Dr(challenge.Value + 2);
                break;
            case AbilityKind.Presence when HasSmashedFace:
                penalty += Dice.Roll(DiceExpr.D4);
                break;
        }

        switch (ability)
        {
            case AbilityKind.Strength when HasSeveredArm:
                challenge = new Dr(challenge.Value + 4);
                break;
            case AbilityKind.Strength when HasBrokenHand:
            case AbilityKind.Agility when HasStabbedLung || HasCrushedFoot:
                challenge = new Dr(challenge.Value + 2);
                break;
        }

        if (ability is AbilityKind.Agility && HasLostEye) penalty += Dice.Roll(DiceExpr.D4);

        challenge = new Dr(challenge.Value + penalty);

        var rollResults = Dice.Roll(DiceExpr.D20);
        var outcome = rollResults + Abilities[ability].Modifier;
        var nat = rollResults switch { 1 => Natural.One, 20 => Natural.Twenty, _ => Natural.None };

        return nat is Natural.One ? ChallengeOutcome.Fail(nat)
            : nat is Natural.Twenty ? ChallengeOutcome.Success(nat)
            : outcome >= challenge.Value ? ChallengeOutcome.Success(nat) : ChallengeOutcome.Fail(nat);
    }

    public void Improve(AbilityKind kind, int delta)
    {
        Abilities.ModifyAbility(kind, delta);

        if (kind != AbilityKind.Strength)
            return;
        var newCapacity = 2 * (Abilities.Strength.Modifier + 8);
        Inventory = Inventory with { MaxCapacity = newCapacity };
    }

    public void Degrade(AbilityKind kind, int delta)
    {
        if (delta >= 0) throw new InvalidOperationException("Degrade delta must be negative.");

        Abilities.ModifyAbility(kind, delta);

        if (kind != AbilityKind.Strength)
            return;
        var newCapacity = 2 * (Abilities.Strength.Modifier + 8);
        Inventory = Inventory with { MaxCapacity = newCapacity };
    }

    public static Character Create(Guid id, string name, int maxHp, Abilities.Abilities abilities,
        StartingEquipment equipment, int startingOmensCount = 0)
    {
        var items = new List<InventoryItem>();

        if (equipment.Gear1 is not null) items.Add(equipment.Gear1);

        if (equipment.Gear2 is not null) items.Add(equipment.Gear2);
        var inventoryCapacity = 2 * (abilities.Strength.Modifier + 8);

        return new Character(
            id,
            name,
            abilities,
            equipment.Silver,
            equipment.FoodDays,
            new Inventory(equipment.Container, inventoryCapacity, items),
            equipment.Weapon,
            equipment.Armor,
            equipment.Shield,
            equipment.Scrolls,
            PowerPool.Create(abilities),
            maxHp,
            maxHp,
            startingOmensCount);
    }
}