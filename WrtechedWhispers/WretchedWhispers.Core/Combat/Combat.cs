using WretchedWhispers.Core.Character.Armor;
using WretchedWhispers.Core.Character.Armor.Tiers;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Test;

namespace WretchedWhispers.Core.Combat;

public static class Combat
{
    public static AttackOutcome ResolvePlayerAttack(IRandomService rng, Abilities.Abilities abilities,
        AttackRequest req, Armor targetArmor)
    {
        var ability = req.Kind == AttackKind.Melee ? abilities.Strength : abilities.Presence;
        var test = Test.Test.Roll(rng, ability, req.BaseDr);

        var hit = test.Outcome == TestOutcome.Success;
        var crit = test.IsCrit;
        var fumble = test.IsFumble;
        var weaponBroken = false;
        var targetArmorDegraded = false;

        var dmg = Damage.Zero;
        if (hit)
        {
            var raw = rng.Roll(req.Weapon.DamageDie);
            if (crit) raw *= 2;
            // Armor damage reduction
            var reduction = targetArmor.DamageReduction.Sides == 0 ? 0 : rng.Roll(targetArmor.DamageReduction);
            var final = Math.Max(0, raw - reduction);
            dmg = Damage.From(final);
            if (crit && targetArmor.Tier is not NoArmorTier)
            {
                targetArmorDegraded = true;
                targetArmor.Degrade();
            }
        }
        else if (fumble)
        {
            // Weapon breaks or is lost — we model as broken
            weaponBroken = true;
        }

        return new AttackOutcome(hit, dmg, crit, fumble, weaponBroken, targetArmorDegraded);
    }

    public static DefenceOutcome ResolvePlayerDefence(IRandomService rng, Abilities.Abilities abilities,
        DefenceRequest req, Armor wornArmor)
    {
        // Base DR12, modified by armor penalties
        var dr = new Dr(req.BaseDr.Value + wornArmor.DefencePenalty);
        var test = Test.Test.Roll(rng, abilities.Agility, dr);
        var avoided = test.Outcome == TestOutcome.Success;
        var critFree = test.IsCrit; // free attack granted to the PC
        var fumble = test.IsFumble;
        var armorDegraded = false;

        if (fumble && wornArmor.Tier is not NoArmorTier)
        {
            armorDegraded = true;
            wornArmor.Degrade();
        }

        return new DefenceOutcome(avoided, critFree, fumble, armorDegraded);
    }
}