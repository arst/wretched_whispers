using System.Text.Json.Serialization;
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
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Characters.Status.Broken;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public sealed class Character
{
    [JsonIgnore] private List<Scroll> _scrolls;

    [JsonConstructor]
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
        HitPoints hp,
        Omens omens,
        InjurySet injuries = default,
        bool isInfected = false,
        bool isDizzyFromMagic = false,
        bool isDead = false)
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
        Omens = omens;
        Hp = hp;
        _scrolls = scrolls;
        Inventory = inventory;
        Injuries = injuries;
        IsInfected = isInfected;
        IsDizzyFromMagic = isDizzyFromMagic;
        IsDead = isDead;
    }

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
        : this(id, name, abilities, silver, foodDays, inventory, weapon, armor, shield,
            scrolls, powers, new HitPoints(currentHp, maxHp), new Omens(omenCount))
    {
    }

    [JsonInclude] public Guid Id { get; private set; }
    [JsonInclude] public string Name { get; private set; }
    [JsonInclude] public Abilities.Abilities Abilities { get; private set; }
    [JsonInclude] public int Silver { get; private set; }
    public int FoodDays { get; }

    [JsonInclude] public Inventory Inventory { get; private set; }

    [JsonInclude] public HitPoints Hp { get; private set; }
    public Armor Armor { get; }
    public Shield? Shield { get; }
    [JsonInclude] public Weapon Weapon { get; private set; }
    [JsonInclude] public Omens Omens { get; private set; }
    public PowerPool Powers { get; }

    [JsonInclude] public bool IsInfected { get; private set; }
    [JsonInclude] public bool IsDizzyFromMagic { get; private set; }
    [JsonIgnore] public bool IsEncumbered => Inventory.IsEncumbered(Abilities.Strength);

    [JsonInclude] public bool IsDead { get; private set; }

    [JsonInclude] public InjurySet Injuries { get; private set; }

    // Backward-compatible computed properties for CharacterPlugin and other consumers
    [JsonIgnore] public bool HasLostEye => Injuries.Has(InjuryKind.LostEye);
    [JsonIgnore] public bool HasStabbedLung => Injuries.Has(InjuryKind.StabbedLung);
    [JsonIgnore] public bool HasBrokenHand => Injuries.Has(InjuryKind.BrokenHand);
    [JsonIgnore] public bool HasCrushedFoot => Injuries.Has(InjuryKind.CrushedFoot);
    [JsonIgnore] public bool HasSeveredArm => Injuries.Has(InjuryKind.SeveredArm);
    [JsonIgnore] public bool HasSmashedFace => Injuries.Has(InjuryKind.SmashedFace);

    [JsonInclude] public List<Scroll> Scrolls { get => _scrolls; private set => _scrolls = value; }

    public void Infect()
    {
        IsInfected = true;
    }

    public void CureInfection()
    {
        IsInfected = false;
    }

    public void StartNewDay(Dice dice)
    {
        Powers.ResetForNewDay(Abilities, dice);
        IsDizzyFromMagic = false;
    }

    public AttackOutcome Attack(Armor targetArmor, Dice dice)
    {
        var outcome = ResolveAttack(targetArmor, dice);

        if (outcome.Fumble)
            // Weapon breaks => fallback to Improvised
            Weapon = Weapon.Create(WeaponKind.Improvised);

        if (outcome.TargetArmorDegraded) targetArmor.Degrade();

        return new AttackOutcome(outcome.Hit, outcome.Damage, outcome.Critical, outcome.Fumble, outcome.WeaponBroken,
            outcome.TargetArmorDegraded);
    }

    private AttackOutcome ResolveAttack(Armor targetArmor, Dice dice)
    {
        var abilityKind = Weapon.IsRanged ? AbilityKind.Presence : AbilityKind.Strength;
        var test = Challenge(new Dr(12), abilityKind, dice);
        var hit = test.IsSuccess;
        var crit = test.Natural == Natural.Twenty;
        var fumble = test.Natural == Natural.One;
        var weaponBroken = false;
        var targetArmorDegraded = false;

        var dmg = Damage.Zero;
        if (hit)
        {
            var raw = dice.Roll(Weapon.DamageDie);
            if (crit) raw *= 2;
            // Armor damage reduction delegated to ArmorTier
            var reduction = targetArmor.Tier.RollDamageReduction(dice);
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

    public DefenceOutcome Defend(DiceExpr attackDie, Dice dice)
    {
        var outcome = ResolveDefence(dice);

        if (outcome.IsAvoided)
            return new DefenceOutcome
            {
                DamageDealt = 0,
                Avoided = outcome.IsAvoided,
                CriticalFreeAttack = outcome.IsCritFree,
                FumbleDoubleDamage = outcome.IsFumble
            };

        var damage = CalculateDamageAfterDefense(attackDie, outcome, dice);
        ReceiveDamage(damage, dice);

        // TODO: Implement armor tier degradation + shield break

        return new DefenceOutcome
        {
            DamageDealt = damage,
            Avoided = outcome.IsAvoided,
            CriticalFreeAttack = outcome.IsCritFree,
            FumbleDoubleDamage = outcome.IsFumble
        };
    }

    private void ReceiveDamage(int damage, Dice dice)
    {
        Hp = Hp.Damage(damage);

        if (Hp.IsZero)
        {
            var brokenOutcome = ResolveBroken(dice);
            switch (brokenOutcome)
            {
                case null:
                    break;
                case DeadBroken:
                    IsDead = true;
                    break;
                case BrokenHand:
                    Injuries = Injuries.Add(InjuryKind.BrokenHand);
                    break;
                case CrushedFoot:
                    Injuries = Injuries.Add(InjuryKind.CrushedFoot);
                    break;
                case EyeLost:
                    Injuries = Injuries.Add(InjuryKind.LostEye);
                    break;
                case SeveredArm:
                    Injuries = Injuries.Add(InjuryKind.SeveredArm);
                    break;
                case SmashedFace:
                    Injuries = Injuries.Add(InjuryKind.SmashedFace);
                    break;
                case StabbedLung:
                    Injuries = Injuries.Add(InjuryKind.StabbedLung);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(brokenOutcome));
            }
        }
    }


    private int CalculateDamageAfterDefense(DiceExpr attackDie,
        (bool IsAvoided, bool IsCritFree, bool IsFumble) outcome, Dice dice)
    {
        var damage = dice.Roll(attackDie);

        if (outcome.IsFumble) damage *= 2; // Fumble doubles the damage

        if (outcome.IsCritFree)
        {
            var freeAttackResults = Defend(attackDie, dice); // Crit grants a free attack
            damage += freeAttackResults.DamageDealt;
        }

        var armorReduction =
            Armor.Tier.RollDamageReduction(dice) +
            (Shield is not null
                ? 1
                : 0); // Shield adds +1 to armor reduction or completely blocks one attack and breaks, model as +1 to armor reduction fo now

        damage -= armorReduction;
        return damage;
    }

    private (bool IsAvoided, bool IsCritFree, bool IsFumble) ResolveDefence(Dice dice)
    {
        var dr = new Dr(new Dr(12).Value + Armor.DefencePenalty);
        var test = Challenge(dr, AbilityKind.Agility, dice, Armor.AgilityPenalty);
        var avoided = test.IsSuccess;
        var critFree = test.Natural == Natural.Twenty;
        var fumble = test.Natural == Natural.One;

        return (avoided, critFree, fumble);
    }

    private BrokenOutcome? ResolveBroken(Dice dice)
    {
        if (!Hp.IsZero)
            return null;

        var d4 = dice.Roll(DiceExpr.D4);

        if (d4 is 1 or 2) return BrokenOutcome.Dead();

        if (d4 > 4) return null;

        var d6 = dice.Roll(DiceExpr.D6);

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

    public void Rest(int hours, Dice dice)
    {
        if (IsInfected)
        {
            ReceiveDamage(dice.Roll(DiceExpr.D6), dice);
            return;
        }

        var isFullNightRest = hours >= 8;
        var heal = isFullNightRest ? dice.Roll(DiceExpr.D6) : dice.Roll(DiceExpr.D4);
        Hp = Hp.Heal(heal);
    }

    public void BuyItem(int price, InventoryItem item)
    {
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

        if (Inventory.IsFull)
            throw new InvalidOperationException("Inventory is full, throw away another item to add a new one.");

        if (Silver < price) throw new InvalidOperationException("Not enough silver to buy the item.");

        Inventory.AddItem(item);
        Silver -= price;
    }

    public CastOutcome Cast(Guid scrollId, Dice dice)
    {
        var scroll = _scrolls.FirstOrDefault(s => s.Id == scrollId);

        if (scroll is null) throw new InvalidOperationException("Scroll not found in inventory.");

        if (IsDizzyFromMagic)
            return CastOutcome.Fail("Dizzy from prior failure");
        if (!ScrollRestrictionPolicy.CanUseScrolls(Weapon, Armor))
            return CastOutcome.Fail("Can't use scrolls, because armor is too heavy or weapon is two-handed");
        if (!Powers.TryConsumeOne())
            return CastOutcome.Fail("No daily power uses remaining");

        var challengeOutcome = Challenge(new Dr(12), AbilityKind.Presence, dice);

        if (challengeOutcome.IsSuccess) return CastOutcome.Success(scroll.Description);

        var loss = dice.Roll(DiceExpr.D2);
        ReceiveDamage(loss, dice);
        IsDizzyFromMagic = true;
        return CastOutcome.Fizzle(scroll.Description, loss);
    }

    public ChallengeOutcome Challenge(Dr challenge, AbilityKind ability, Dice dice, int penalty = 0)
    {
        // Encumbrance penalty
        if (ability is AbilityKind.Strength or AbilityKind.Agility && IsEncumbered)
            challenge = new Dr(challenge.Value + 2);

        // Injury-based DR increases (fixed penalties)
        switch (ability)
        {
            case AbilityKind.Strength:
                challenge = new Dr(challenge.Value + Injuries.GetStrengthPenalty());
                break;
            case AbilityKind.Agility:
                challenge = new Dr(challenge.Value + Injuries.GetAgilityPenalty());
                break;
        }

        // Injury-based dice penalties
        var presencePenaltyDice = Injuries.GetPresencePenaltyDice();
        if (ability is AbilityKind.Presence && presencePenaltyDice.Sides > 0)
            penalty += dice.Roll(presencePenaltyDice);

        var agilityPenaltyDice = Injuries.GetAgilityPenaltyDice();
        if (ability is AbilityKind.Agility && agilityPenaltyDice.Sides > 0)
            penalty += dice.Roll(agilityPenaltyDice);

        challenge = new Dr(challenge.Value + penalty);

        var rollResults = dice.Roll(DiceExpr.D20);
        var outcome = rollResults + Abilities[ability].Modifier;
        var nat = rollResults switch { 1 => Natural.One, 20 => Natural.Twenty, _ => Natural.None };

        return nat is Natural.One ? ChallengeOutcome.Fail(nat)
            : nat is Natural.Twenty ? ChallengeOutcome.Success(nat)
            : outcome >= challenge.Value ? ChallengeOutcome.Success(nat) : ChallengeOutcome.Fail(nat);
    }

    public void Improve(AbilityKind kind, int delta)
    {
        Abilities = Abilities.ModifyAbility(kind, delta);

        if (kind != AbilityKind.Strength)
            return;
        var newCapacity = 2 * (Abilities.Strength.Modifier + 8);
        Inventory.MaxCapacity = newCapacity;
    }

    public void Degrade(AbilityKind kind, int delta)
    {
        if (delta >= 0) throw new InvalidOperationException("Degrade delta must be negative.");

        Abilities = Abilities.ModifyAbility(kind, delta);

        if (kind != AbilityKind.Strength)
            return;
        var newCapacity = 2 * (Abilities.Strength.Modifier + 8);
        Inventory.MaxCapacity = newCapacity;
    }

    public static Character Create(Guid id, string name, int maxHp, Abilities.Abilities abilities,
        StartingEquipment equipment, Dice dice, int startingOmensCount = 0)
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
            PowerPool.Create(abilities, dice),
            maxHp,
            maxHp,
            startingOmensCount);
    }
}
