using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using Scroll = WretchedWhispers.Core.Characters.Possessions.Scrolls.Scroll;

namespace WretchedWhispers.Core.Characters.Create;

public class CharacterCreationService(ICharactersRepository charactersRepository)
{
    public async Task<Character> Create(string name)
    {
        var id = Guid.NewGuid();
        var abilities = RollAbilities();
        var equipment = RollStartingEquipment(abilities);
        var maxHp = RollStartingHealthPoints(abilities);
        const int numberOfOmens = 0; // TODO: Implement as d2 roll when enabled

        var character = Character.Create(id, name, maxHp, abilities, equipment);
        await charactersRepository.Save(character);

        return character;
    }

    private int RollStartingHealthPoints(Abilities.Abilities abilities)
    {
        return Math.Max(1, abilities.Toughness.Modifier + Dice.Roll(DiceExpr.D8));
    }

    private StartingEquipment RollStartingEquipment(Abilities.Abilities abilities)
    {
        var silver = Dice.Roll(DiceExpr.D(2, 6)) * 10; // 2d6 × 10 silver
        var foodDays = Dice.Roll(DiceExpr.D4); // d4 days of food
        var container = RollContainer(); // d6: nothing/backpack/sack/wagon/donkey
        var gear1 = RollGearSlot1(abilities);
        var gear2 = RollGearSlot2();
        var hasScroll = gear1.ScrollSchool is not null || gear2.ScrollSchool is not null;
        var hasShield = gear1.IsShield || gear2.IsShield;

        var weapon = RollWeapon(hasScroll);
        var armor = RollArmor(hasScroll);
        var scrolls = new List<Scroll>();

        if (gear1.ScrollSchool is not null) scrolls.Add(new Scroll(Guid.NewGuid(), gear1.ScrollSchool.Value, "random"));

        if (gear2.ScrollSchool is not null) scrolls.Add(new Scroll(Guid.NewGuid(), gear2.ScrollSchool.Value, "random"));

        return new StartingEquipment(
            silver,
            foodDays,
            container,
            gear1.ScrollSchool is null && !gear1.IsShield
                ? new InventoryItem(Guid.NewGuid(), gear1.GearDescription, false, true, gear1.Quantity)
                : null,
            gear2.ScrollSchool is null && !gear2.IsShield
                ? new InventoryItem(Guid.NewGuid(), gear2.GearDescription, false, true, gear2.Quantity)
                : null,
            weapon,
            armor,
            hasShield ? new Shield() : null,
            scrolls
        );
    }

    private static string RollContainer()
    {
        var containerRoll = Dice.Roll(DiceExpr.D6);
        return containerRoll switch
        {
            1 or 2 => "nothing",
            3 => "backpack (7 items)",
            4 => "sack (10 items)",
            5 => "small wagon (or choose one above)",
            6 => "donkey (or choose one above)",
            _ => throw new ArgumentOutOfRangeException(nameof(containerRoll))
        };
    }

    private static (string GearDescription, Possessions.Scrolls.ScrollSchool? ScrollSchool, bool IsShield, int Quantity)
        RollGearSlot1(
            Abilities.Abilities abilities)
    {
        var d = Dice.Roll(DiceExpr.D12);
        return d switch
        {
            1 => ("rope 30 ft", null, false, 1),
            2 => ("torches", null, false, abilities.Presence.Modifier + 4),
            3 => ("lantern + oil", null, false, abilities.Presence.Modifier + 6),
            4 => ("magnesium strip", null, false, 1),
            5 => ("random unclean scroll", ScrollSchool: ScrollSchool.Unclean, false, 1),
            6 => ("sharp needle", null, false, 1),
            7 => ("medicine chest (Presence + 4 uses)", null, false, abilities.Presence.Modifier + 4),
            8 => ("metal file & lockpicks", null, false, 1),
            9 => ("bear trap (dr14 to spot, d8 dmg)", null, false, 1),
            10 => ("bomb (sealed bottle, d10 dmg)", null, false, 1),
            11 => ("red poison (d4 doses)", null, false, 1),
            _ => ("silver crucifix", null, false, 1)
        };
    }

    private static (string GearDescription, Possessions.Scrolls.ScrollSchool? ScrollSchool, bool IsShield, int Quantity)
        RollGearSlot2()
    {
        var d = Dice.Roll(DiceExpr.D12);
        return d switch
        {
            1 => ("life elixir", null, false, Dice.Roll(DiceExpr.D4)),
            2 => ("random sacred scroll", ScrollSchool: ScrollSchool.Sacred, false, 1),
            3 => ("small but vicious dog", null, false, 1),
            4 => ("monkeys that ignore but love you", null, false, Dice.Roll(DiceExpr.D4)),
            5 => ("exquisite perfume (25s)", null, false, 1),
            6 => ("toolbox", null, false, 1),
            7 => ("heavy chain 15 ft", null, false, 1),
            8 => ("grappling hook", null, false, 1),
            9 => ("shield (-1 dmg or break to ignore one attack)", null, true, 1),
            10 => ("crowbar (d4 dmg)", null, false, 1),
            11 => ("lard (5 meals)", null, false, 5),
            _ => ("tent", null, false, 1)
        };
    }

    private Weapon RollWeapon(bool hasScroll)
    {
        // Weapons d10 (d6 if you begin with a scroll)
        var d = hasScroll ? Dice.Roll(DiceExpr.D6) : Dice.Roll(DiceExpr.D10);
        return Weapon.Create(d switch
        {
            1 => WeaponKind.Femur,
            2 => WeaponKind.Staff,
            3 => WeaponKind.ShortSword,
            4 => WeaponKind.Knife,
            5 => WeaponKind.Warhammer,
            6 => WeaponKind.Sword,
            7 => WeaponKind.Bow,
            8 => WeaponKind.Flail,
            9 => WeaponKind.Crossbow,
            10 => WeaponKind.Zweihander,
            _ => WeaponKind.Femur
        });
    }

    private Armor RollArmor(bool hasScroll)
    {
        // Armor d4 (d2 if you begin with a scroll)
        var d = hasScroll ? Dice.Roll(DiceExpr.D2) : Dice.Roll(DiceExpr.D4);
        ArmorTier tier = d switch
        {
            1 => NoArmorTier.Instance,
            2 => LightArmorTier.Instance,
            3 => MediumArmorTier.Instance,
            4 => HeavyArmorTier.Instance,
            _ => NoArmorTier.Instance
        };
        return new Armor(tier);
    }

    private Abilities.Abilities RollAbilities()
    {
        return new Abilities.Abilities(
            RollToAbilityScoreMap(Roll()),
            RollToAbilityScoreMap(Roll()),
            RollToAbilityScoreMap(Roll()),
            RollToAbilityScoreMap(Roll())
        );

        AbilityScore RollToAbilityScoreMap(int sum)
        {
            return sum switch
            {
                <= 4 => new AbilityScore(-3),
                <= 6 => new AbilityScore(-2),
                <= 8 => new AbilityScore(-1),
                <= 12 => new AbilityScore(0),
                <= 14 => new AbilityScore(+1),
                <= 16 => new AbilityScore(+2),
                _ => new AbilityScore(+3)
            };
        }

        int Roll()
        {
            return Dice.Roll(DiceExpr.D(3, 6));
        }
    }
}