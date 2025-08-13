using WretchedWhispers.Core.Characters.Armor;
using WretchedWhispers.Core.Characters.Armor.Tiers;
using WretchedWhispers.Core.Characters.Weapon;
using WretchedWhispers.Core.Combat.Attack;
using WretchedWhispers.Core.Combat.Defence;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Test;

namespace WretchedWhispers.Core.Combat;

public static class Combat
{
    public static AttackResolutionOutcome ResolvePlayerAttack(Dice.Dice rng, Abilities.Abilities abilities,
        Weapon attackingWeapon, Armor targetArmor)
    {
        var ability = attackingWeapon.IsRanged ? abilities.Presence : abilities.Strength;
        var test = Test.Test.Roll(rng, ability, new Dr(12));

        var hit = test.Outcome == TestOutcome.Success;
        var crit = test.IsCrit;
        var fumble = test.IsFumble;
        var weaponBroken = false;
        var targetArmorDegraded = false;

        var dmg = Damage.Zero;
        if (hit)
        {
            var raw = rng.Roll(attackingWeapon.DamageDie);
            if (crit) raw *= 2;
            // Armor damage reduction
            var reduction = targetArmor.DamageReduction.Sides == 0 ? 0 : rng.Roll(targetArmor.DamageReduction);
            var final = Math.Max(0, raw - reduction);
            dmg = Damage.From(final);

            if (crit && targetArmor.Tier is not NoArmorTier) targetArmorDegraded = true;
        }
        else if (fumble)
        {
            // Weapon breaks or is lost, model as broken for now
            weaponBroken = true;
        }

        return new AttackResolutionOutcome(hit, dmg, crit, fumble, weaponBroken, targetArmorDegraded);
    }

    public static DefenceResolutionOutcome ResolvePlayerDefence(Dice.Dice rng, Abilities.Abilities abilities,
        DefenceRequest req, Armor wornArmor)
    {
        var dr = new Dr(req.BaseDr.Value + wornArmor.DefencePenalty);
        var test = Test.Test.Roll(rng, abilities.Agility, dr);
        var avoided = test.Outcome == TestOutcome.Success;
        var critFree = test.IsCrit; // free attack granted to the attacker
        var fumble = test.IsFumble;

        return new DefenceResolutionOutcome(avoided, critFree, fumble);
    }
}