using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Armor;
using WretchedWhispers.Core.Characters.Armor.Tiers;
using WretchedWhispers.Core.Characters.Weapon;
using WretchedWhispers.Core.Dice;
using WretchedWhispers.Core.Scrolls;

namespace WretchedWhispers.Core.CharacterCreation;

public class CharacterCreationService(IRandomService randomService, ICharactersRepository charactersRepository)
    : ICharacterCreationService
{
    public async Task<Character> Create(string name)
    {
        var id = Guid.NewGuid();
        var abilities = RollAbilities();
        var equipment = RollStartingEquipment();
        var maxHp = RollStartingHealthPoints(abilities);
        const int numberOfOmens = 0; // TODO: Implement as d2 roll when enabled

        var character = Character.Create(id, name, maxHp, abilities, equipment);
        await charactersRepository.SaveAsync(character);

        return character;
    }

    private int RollStartingHealthPoints(Abilities.Abilities abilities)
    {
        return Math.Max(1, abilities.Toughness.Modifier + randomService.D(8));
    }

    private StartingEquipment RollStartingEquipment()
    {
        var silver = randomService.D(2, 6) * 10; // 2d6 × 10 silver
        var foodDays = randomService.D(4); // d4 days of food
        var container = RollContainer(); // d6: nothing/backpack/sack/wagon/donkey
        var gear1 = RollGearSlot1();
        var gear2 = RollGearSlot2();
        var hasScroll = gear1.ScrollSchool is not null || gear2.ScrollSchool is not null;
        var hasShield = gear1.IsShield || gear2.IsShield;

        var weapon = RollWeapon(hasScroll);
        var armor = RollArmor(hasScroll);
        var scrolls = new List<Scroll>();

        if (gear1.ScrollSchool is not null) scrolls.Add(new Scroll(gear1.ScrollSchool.Value, "random"));

        if (gear2.ScrollSchool is not null) scrolls.Add(new Scroll(gear2.ScrollSchool.Value, "random"));

        return new StartingEquipment(
            silver,
            foodDays,
            container,
            gear1.GearDescription,
            gear2.GearDescription,
            weapon,
            armor,
            hasShield ? new Shield() : null,
            scrolls
        );
    }

    private string RollContainer()
    {
        var containerRoll = randomService.D(1, 6);
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

    private (string GearDescription, Scrolls.ScrollSchool? ScrollSchool, bool IsShield) RollGearSlot1()
    {
        var d = randomService.D(12);
        return d switch
        {
            1 => ("rope 30 ft", null, false),
            2 => ("torches (Presence + 4)", null, false),
            3 => ("lantern + oil (Presence + 6 hours)", null, false),
            4 => ("magnesium strip", null, false),
            5 => ("random unclean scroll", ScrollSchool: ScrollSchool.Unclean, false),
            6 => ("sharp needle", null, false),
            7 => ("medicine chest (Presence + 4 uses)", null, false),
            8 => ("metal file & lockpicks", null, false),
            9 => ("bear trap (dr14 to spot, d8 dmg)", null, false),
            10 => ("bomb (sealed bottle, d10 dmg)", null, false),
            11 => ("red poison (d4 doses)", null, false),
            _ => ("silver crucifix", null, false)
        };
    }

    private (string GearDescription, Scrolls.ScrollSchool? ScrollSchool, bool IsShield) RollGearSlot2()
    {
        var d = randomService.D(12);
        return d switch
        {
            1 => ("life elixir d4 doses", null, false),
            2 => ("random sacred scroll", ScrollSchool: ScrollSchool.Sacred, false),
            3 => ("small but vicious dog", null, false),
            4 => ("d4 monkeys that ignore but love you", null, false),
            5 => ("exquisite perfume (25s)", null, false),
            6 => ("toolbox", null, false),
            7 => ("heavy chain 15 ft", null, false),
            8 => ("grappling hook", null, false),
            9 => ("shield (-1 dmg or break to ignore one attack)", null, true),
            10 => ("crowbar (d4 dmg)", null, false),
            11 => ("lard (5 meals)", null, false),
            _ => ("tent", null, false)
        };
    }

    private Weapon RollWeapon(bool hasScroll)
    {
        // Weapons d10 (d6 if you begin with a scroll)
        var d = hasScroll ? randomService.D(6) : randomService.D(10);
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
        var d = hasScroll ? randomService.D(2) : randomService.D(4);
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
            return randomService.Roll(DiceExpr.Parse("3d6"));
        }
    }
}