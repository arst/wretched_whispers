using System.Diagnostics;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Cast;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Combat;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Characters.Powers;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Core.Characters;

public sealed class Character
{
    private readonly List<Scroll> _scrolls;

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
        // Param type must equal the property type or STJ refuses to bind the constructor.
        IReadOnlyList<Scroll> scrolls,
        PowerPool powers,
        HitPoints hp,
        Omens omens,
        InjurySet injuries = default,
        bool isInfected = false,
        bool isDizzyFromMagic = false,
        bool isDead = false,
        bool canGetBetter = false,
        CharacterClass @class = CharacterClass.Classless)
    {
        Id = id;
        Name = name;
        Class = @class;
        Abilities = abilities;
        Silver = silver;
        FoodDays = foodDays;
        Weapon = weapon;
        Armor = armor;
        Shield = shield;
        Powers = powers;
        Omens = omens;
        Hp = hp;
        _scrolls = scrolls?.ToList() ?? [];
        Inventory = inventory;
        Injuries = injuries;
        IsInfected = isInfected;
        IsDizzyFromMagic = isDizzyFromMagic;
        IsDead = isDead;
        CanGetBetter = canGetBetter;
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
        int omenCount = 0,
        CharacterClass @class = CharacterClass.Classless)
        : this(id, name, abilities, silver, foodDays, inventory, weapon, armor, shield,
            scrolls, powers, new HitPoints(currentHp, maxHp), new Omens(omenCount), @class: @class)
    {
    }

    [JsonInclude] public Guid Id { get; private set; }
    [JsonInclude] public string Name { get; private set; }

    /// <summary>What kind of wretch this is. Immutable after creation. Defaults to
    /// <see cref="CharacterClass.Classless"/>, which is also what pre-class saved blobs deserialize to.</summary>
    [JsonInclude] public CharacterClass Class { get; private set; }
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

    /// <summary>MORK BORG "Getting Better" gate: set by a full night's rest, consumed by the ritual.</summary>
    [JsonInclude] public bool CanGetBetter { get; private set; }

    [JsonInclude] public InjurySet Injuries { get; private set; }

    [JsonIgnore] public bool HasLostEye => Injuries.Has(InjuryKind.LostEye);
    [JsonIgnore] public bool HasStabbedLung => Injuries.Has(InjuryKind.StabbedLung);
    [JsonIgnore] public bool HasBrokenHand => Injuries.Has(InjuryKind.BrokenHand);
    [JsonIgnore] public bool HasCrushedFoot => Injuries.Has(InjuryKind.CrushedFoot);
    [JsonIgnore] public bool HasSeveredArm => Injuries.Has(InjuryKind.SeveredArm);
    [JsonIgnore] public bool HasSmashedFace => Injuries.Has(InjuryKind.SmashedFace);

    // Read-only projection over the list the constructor binds — mutation goes through aggregate methods.
    [JsonInclude] public IReadOnlyList<Scroll> Scrolls => _scrolls;

    // Aggregate delegate methods for Inventory operations
    public void AddItem(InventoryItem item) => Inventory.AddItem(item);
    public void RemoveItem(Guid itemId) => Inventory.RemoveItem(itemId);
    public bool ConsumeItem(Guid itemId) => Inventory.ConsumeItem(itemId);
    public void ReplenishItem(Guid itemId, int amount = 1) => Inventory.ReplenishItem(itemId, amount);

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

    public AttackOutcome Attack(Armor targetArmor, Dice dice, bool spendOmenForMaxDamage = false,
        bool minimumOneDamage = false)
    {
        // Pre-declared spend: the omen is consumed before the roll, even if the attack misses.
        if (spendOmenForMaxDamage && !Omens.TrySpend())
            throw new InvalidOperationException("No omens remaining.");

        var outcome = ResolveAttack(targetArmor, dice, spendOmenForMaxDamage, minimumOneDamage);

        if (outcome.Fumble)
            // Weapon breaks => fallback to Improvised
            Weapon = Weapon.Create(WeaponKind.Improvised);

        if (outcome.TargetArmorDegraded) targetArmor.Degrade();

        return outcome;
    }

    private AttackOutcome ResolveAttack(Armor targetArmor, Dice dice, bool spendOmenForMaxDamage = false,
        bool minimumOneDamage = false)
    {
        var abilityKind = Weapon.IsRanged ? AbilityKind.Presence : AbilityKind.Strength;
        var test = Challenge(new Dr(12), abilityKind, dice);
        var hit = test.IsSuccess;
        var crit = test.Natural == Natural.Twenty;
        var fumble = test.Natural == Natural.One;
        var weaponBroken = false;
        var targetArmorDegraded = false;

        var dmg = Damage.Zero;
        var baseRoll = 0;
        var reduction = 0;
        if (hit)
        {
            baseRoll = spendOmenForMaxDamage ? Weapon.DamageDie.Max : dice.Roll(Weapon.DamageDie);
            var raw = crit ? baseRoll * 2 : baseRoll;
            // Armor damage reduction delegated to ArmorTier
            reduction = targetArmor.Tier.RollDamageReduction(dice);
            // Forgiving difficulties floor a landed blow at 1: armor may blunt the swing but never
            // swallow it whole, which is what makes a weak weapon feel like hitting a wall.
            var final = Math.Max(minimumOneDamage ? 1 : 0, raw - reduction);
            dmg = Damage.From(final);

            if (crit && targetArmor.Tier is not ArmorTier.None) targetArmorDegraded = true;
        }
        else if (fumble)
        {
            // Weapon breaks or is lost, model as broken for now
            weaponBroken = true;
        }

        return new AttackOutcome(hit, dmg, crit, fumble, weaponBroken, targetArmorDegraded, baseRoll, reduction,
            test.Roll, test.Modifier, test.EffectiveDr, abilityKind);
    }

    public DefenceOutcome Defend(DiceExpr attackDie, Dice dice, bool spendOmenToReduceDamage = false)
    {
        var test = Challenge(new Dr(12 + Armor.DefencePenalty), AbilityKind.Agility, dice, Armor.AgilityPenalty);
        var isFumble = test.Natural == Natural.One;

        // A natural 20 always avoids, so the crit flag only ever rides the avoided path — the free
        // attack it promises is the narrator's to grant, not a domain-resolved swing.
        if (test.IsSuccess)
            return new DefenceOutcome
            {
                DamageDealt = 0,
                Avoided = true,
                CriticalFreeAttack = test.Natural == Natural.Twenty,
                FumbleDoubleDamage = isFumble,
                Roll = test.Roll,
                Modifier = test.Modifier,
                EffectiveDr = test.EffectiveDr
            };

        var (damage, baseRoll, armorReduction) = CalculateDamageAfterDefense(attackDie, isFumble, dice);

        // Silent TrySpend: the model-facing "no omens" guard lives in ResolveRound before any
        // mutation — throwing here would corrupt a half-resolved round.
        var omenReduction = 0;
        if (spendOmenToReduceDamage && damage > 0 && Omens.TrySpend())
        {
            omenReduction = dice.Roll(DiceExpr.D6);
            damage = Math.Max(0, damage - omenReduction);
        }

        ReceiveDamage(damage, dice);

        return new DefenceOutcome
        {
            DamageDealt = damage,
            Avoided = false,
            CriticalFreeAttack = false,
            FumbleDoubleDamage = isFumble,
            OmenDamageReduction = omenReduction,
            Roll = test.Roll,
            Modifier = test.Modifier,
            EffectiveDr = test.EffectiveDr,
            BaseDamageRoll = baseRoll,
            ArmorReduction = armorReduction
        };
    }

    private void ReceiveDamage(int damage, Dice dice)
    {
        Hp = Hp.Damage(damage);

        if (Hp.IsZero)
        {
            var brokenOutcome = ResolveBroken(dice);
            if (brokenOutcome is null)
                return;

            if (brokenOutcome == InjuryKind.None)
                IsDead = true;
            else
                Injuries = Injuries.Add(brokenOutcome.Value);
        }
    }


    private (int Damage, int BaseRoll, int ArmorReduction) CalculateDamageAfterDefense(
        DiceExpr attackDie, bool isFumble, Dice dice)
    {
        var baseRoll = dice.Roll(attackDie);
        var damage = isFumble ? baseRoll * 2 : baseRoll; // Fumble doubles the damage

        // ponytail: shield is a flat +1 to reduction; the break-to-ignore-one-attack choice needs a
        // player decision in the round, add it when defence outcomes get consequences.
        var armorReduction = Armor.Tier.RollDamageReduction(dice) + (Shield is not null ? 1 : 0);

        return (Math.Max(0, damage - armorReduction), baseRoll, armorReduction);
    }

    private InjuryKind? ResolveBroken(Dice dice)
    {
        if (!Hp.IsZero)
            return null;

        var d4 = dice.Roll(DiceExpr.D4);

        if (d4 is 1 or 2) return InjuryKind.None;

        var d6 = dice.Roll(DiceExpr.D6);

        return d6 switch
        {
            1 => InjuryKind.SeveredArm,
            2 => InjuryKind.CrushedFoot,
            3 => InjuryKind.SmashedFace,
            4 => InjuryKind.StabbedLung,
            5 => InjuryKind.BrokenHand,
            6 => InjuryKind.LostEye,
            _ => throw new UnreachableException()
        };
    }

    /// <summary>Returns the number of omens refreshed (0 if none). MORK BORG: omens refill (d2)
    /// only after a full night's rest once all are spent.</summary>
    public int Rest(int hours, Dice dice)
    {
        if (IsInfected)
        {
            ReceiveDamage(dice.Roll(DiceExpr.D6), dice);
            return 0;
        }

        var isFullNightRest = hours >= 8;
        var heal = isFullNightRest ? dice.Roll(DiceExpr.D6) : dice.Roll(DiceExpr.D4);
        Hp = Hp.Heal(heal);

        if (isFullNightRest) CanGetBetter = true;

        if (!isFullNightRest || Omens.Count != 0) return 0;
        var refreshed = dice.Roll(DiceExpr.D2);
        Omens.Refill(refreshed);
        return refreshed;
    }

    /// <summary>MORK BORG "Getting Better": roll 6d10 -- meet or beat max HP and it grows by d6
    /// (current HP untouched). Then a d6 against each ability: meet or beat the score for +1 (cap +6);
    /// below it, lose 1 only when the difficulty allows ability loss. Requires a full night's rest
    /// since the last ritual; consumes that rest.</summary>
    public GettingBetterOutcome GetBetter(Dice dice, bool allowAbilityLoss)
    {
        if (!CanGetBetter)
            throw new InvalidOperationException(
                "Getting Better requires a full night's rest since the last ritual.");

        var hpRoll = dice.Roll(DiceExpr.D(6, 10));
        var hpGained = 0;
        if (hpRoll >= Hp.Max)
        {
            hpGained = dice.Roll(DiceExpr.D6);
            Hp = Hp.IncreaseMax(hpGained);
        }

        var changes = new List<AbilityChange>();
        foreach (var kind in new[]
                 { AbilityKind.Strength, AbilityKind.Agility, AbilityKind.Presence, AbilityKind.Toughness })
        {
            var score = Abilities[kind].Modifier;
            var roll = dice.Roll(DiceExpr.D6);
            var delta = roll >= score
                ? score < 6 ? 1 : 0
                : allowAbilityLoss && score > -3 ? -1 : 0;
            if (delta > 0) Improve(kind, delta);
            if (delta < 0) Degrade(kind, delta);
            changes.Add(new AbilityChange(kind, roll, delta, Abilities[kind].Modifier));
        }

        CanGetBetter = false;
        return new GettingBetterOutcome(hpRoll, hpGained, Hp.Max, changes);
    }

    public void BuyItem(int price, InventoryItem item)
    {
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");

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

    public ChallengeOutcome Challenge(Dr challenge, AbilityKind ability, Dice dice, int penalty = 0,
        bool spendOmenToLowerDr = false)
    {
        if (spendOmenToLowerDr)
        {
            if (!Omens.TrySpend()) throw new InvalidOperationException("No omens remaining.");
            challenge = new Dr(challenge.Value - 4);
        }

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

        var modifier = Abilities[ability].Modifier;
        return nat is Natural.One ? ChallengeOutcome.Fail(nat, rollResults, modifier, challenge.Value)
            : nat is Natural.Twenty ? ChallengeOutcome.Success(nat, rollResults, modifier, challenge.Value)
            : outcome >= challenge.Value
                ? ChallengeOutcome.Success(nat, rollResults, modifier, challenge.Value)
                : ChallengeOutcome.Fail(nat, rollResults, modifier, challenge.Value);
    }

    /// <summary>Flee from combat: Agility vs DR 12, hindered by armor (MORK BORG flee rule).</summary>
    public ChallengeOutcome AttemptFlee(Dice dice) =>
        Challenge(new Dr(12), AbilityKind.Agility, dice, Armor.AgilityPenalty);

    public int SufferConsequence(ChallengeConsequence consequence, DifficultySettings settings, Dice dice)
    {
        if (consequence is ChallengeConsequence.None)
            return 0;

        var severityDie = consequence switch
        {
            ChallengeConsequence.Minor => settings.MinorDamage,
            ChallengeConsequence.Serious => settings.SeriousDamage,
            ChallengeConsequence.Deadly => settings.DeadlyDamage,
            _ => throw new ArgumentOutOfRangeException(nameof(consequence))
        };

        var damage = dice.Roll(severityDie);
        ReceiveDamage(damage, dice);
        return damage;
    }

    public void Improve(AbilityKind kind, int delta)
    {
        if (delta <= 0) throw new InvalidOperationException("Improve delta must be positive.");
        ModifyAbility(kind, delta);
    }

    public void Degrade(AbilityKind kind, int delta)
    {
        if (delta >= 0) throw new InvalidOperationException("Degrade delta must be negative.");
        ModifyAbility(kind, delta);
    }

    private void ModifyAbility(AbilityKind kind, int delta)
    {
        Abilities = Abilities.ModifyAbility(kind, delta);
        if (kind == AbilityKind.Strength)
            Inventory.MaxCapacity = Inventory.CapacityFor(Abilities.Strength);
    }

    public static Character Create(Guid id, string name, int maxHp, Abilities.Abilities abilities,
        StartingEquipment equipment, Dice dice, int startingOmensCount = 0,
        CharacterClass characterClass = CharacterClass.Classless)
    {
        var items = new List<InventoryItem>();

        if (equipment.Gear1 is not null) items.Add(equipment.Gear1);

        if (equipment.Gear2 is not null) items.Add(equipment.Gear2);

        if (equipment.ClassKit is not null) items.AddRange(equipment.ClassKit);

        return new Character(
            id,
            name,
            abilities,
            equipment.Silver,
            equipment.FoodDays,
            new Inventory(equipment.Container, Inventory.CapacityFor(abilities.Strength), items),
            equipment.Weapon,
            equipment.Armor,
            equipment.Shield,
            equipment.Scrolls,
            PowerPool.Create(abilities, dice),
            maxHp,
            maxHp,
            startingOmensCount,
            characterClass);
    }
}
