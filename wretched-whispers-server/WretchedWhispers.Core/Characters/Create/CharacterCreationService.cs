using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;
using Scroll = WretchedWhispers.Core.Characters.Possessions.Scrolls.Scroll;

namespace WretchedWhispers.Core.Characters.Create;

public class CharacterCreationService(ICharactersRepository charactersRepository, Dice dice)
{
    public async Task<Character> Create(string name, Difficulty difficulty,
        CharacterClass characterClass = CharacterClass.Classless)
    {
        var id = Guid.NewGuid();
        var settings = ClassPresets.For(characterClass);
        var abilities = RollAbilities(settings);
        var equipment = RollStartingEquipment(abilities, characterClass, settings);
        var maxHp = RollStartingHealthPoints(abilities, settings) + DifficultyPresets.For(difficulty).StartingHpBonus;
        var numberOfOmens = dice.Roll(settings.OmenDie);

        var character = Character.Create(id, name, maxHp, abilities, equipment, dice, numberOfOmens, characterClass);
        await charactersRepository.Save(character);

        return character;
    }

    /// <summary>Rolls one of the six real classes. The domain owns this die, not the narrator --
    /// <see cref="CharacterClass.Classless"/> is a deliberate choice and never a roll result.</summary>
    public CharacterClass RollRandomClass()
    {
        var roll = dice.Roll(DiceExpr.D(1, ClassPresets.Rollable.Length));
        return ClassPresets.Rollable[roll - 1];
    }

    private int RollStartingHealthPoints(Abilities.Abilities abilities, ClassSettings settings)
    {
        return Math.Max(1, abilities.Toughness.Modifier + dice.Roll(settings.HpDie));
    }

    private StartingEquipment RollStartingEquipment(Abilities.Abilities abilities,
        CharacterClass characterClass, ClassSettings settings)
    {
        var silver = dice.Roll(settings.SilverDice) * 10;
        var foodDays = dice.Roll(DiceExpr.D4); // d4 days of food
        var container = RollContainer(); // d6: nothing/backpack/sack/wagon/donkey
        var gear1 = RollGearSlot1(abilities);
        var gear2 = RollGearSlot2();
        // An illiterate class rolls the same tables, but a scroll is just paper: it stays in the pack as
        // an ordinary item, grants no Power, and costs them none of the armour a real caster pays.
        var gear1Scroll = settings.CanUseScrolls ? gear1.ScrollSchool : null;
        var gear2Scroll = settings.CanUseScrolls ? gear2.ScrollSchool : null;
        // Class-granted scrolls count toward the "began with a scroll" gate below: a wretch who starts
        // able to cast starts worse-armed, the same trade the gear tables already make.
        var hasScroll = gear1Scroll is not null || gear2Scroll is not null
                        || settings.StartingScrollCount > 0;
        var hasShield = gear1.IsShield || gear2.IsShield;

        // A natural attack replaces the rolled weapon rather than sitting alongside it -- see ClassSettings.
        var weapon = settings.NaturalWeapon is { } natural
            ? Weapon.Create(natural)
            : RollWeapon(CapForScroll(settings.WeaponDie, DiceExpr.D6, hasScroll));
        var armor = RollArmor(CapForScroll(settings.ArmorDie, DiceExpr.D2, hasScroll));
        var scrolls = new List<Scroll>();

        if (gear1Scroll is not null) scrolls.Add(new Scroll(Guid.NewGuid(), gear1Scroll.Value, "random"));

        if (gear2Scroll is not null) scrolls.Add(new Scroll(Guid.NewGuid(), gear2Scroll.Value, "random"));

        for (var i = 0; i < settings.StartingScrollCount; i++)
            scrolls.Add(new Scroll(Guid.NewGuid(), settings.StartingScrollSchool ?? RollScrollSchool(), "random"));

        return new StartingEquipment(
            silver,
            foodDays,
            container,
            gear1Scroll is null && !gear1.IsShield
                ? new InventoryItem(Guid.NewGuid(), gear1.GearDescription, false, true, gear1.Quantity)
                : null,
            gear2Scroll is null && !gear2.IsShield
                ? new InventoryItem(Guid.NewGuid(), gear2.GearDescription, false, true, gear2.Quantity)
                : null,
            weapon,
            armor,
            hasShield ? new Shield() : null,
            scrolls,
            RollClassKit(characterClass)
        );
    }

    /// <summary>Class gear on top of the two rolled slots. Lives here rather than in
    /// <see cref="ClassSettings"/> because these are dice tables, and the dice tables live together.
    /// Consumes NO dice for classes without a kit, which keeps classless creation roll-for-roll identical
    /// to what it was before classes existed.</summary>
    private List<InventoryItem>? RollClassKit(CharacterClass characterClass)
    {
        switch (characterClass)
        {
            case CharacterClass.GutterbornScum:
                {
                    var kit = new List<InventoryItem>();
                    var count = dice.Roll(DiceExpr.D4);
                    for (var i = 0; i < count; i++)
                        kit.Add(new InventoryItem(Guid.NewGuid(), RollTrinket(), false, false));
                    return kit;
                }
            case CharacterClass.OccultHerbmaster:
                {
                    var kit = new List<InventoryItem> { new(Guid.NewGuid(), "herb pouch & pestle", false, false) };
                    var count = dice.Roll(DiceExpr.D4);
                    for (var i = 0; i < count; i++)
                        kit.Add(new InventoryItem(Guid.NewGuid(), RollHerb(), false, true));
                    return kit;
                }
            default:
                return null;
        }
    }

    private string RollTrinket()
    {
        return dice.Roll(DiceExpr.D8) switch
        {
            1 => "a child's tooth on a string",
            2 => "a key to a door that burned down",
            3 => "half a portrait of someone hated",
            4 => "a coin of a kingdom that never existed",
            5 => "a dried finger, not yours",
            6 => "a bell with the clapper cut out",
            7 => "a folded note you cannot read",
            _ => "a shard of black mirror"
        };
    }

    private string RollHerb()
    {
        return dice.Roll(DiceExpr.D6) switch
        {
            1 => "gravebloom (numbs pain, blurs the eyes)",
            2 => "iron-root (steadies the hands for an hour)",
            3 => "weeping fungus (purges poison, and everything else)",
            4 => "ash-nettle (wakes the senseless, badly)",
            5 => "corpse-lily (a sleep hard to wake from)",
            _ => "black sedge (stops bleeding, scars foully)"
        };
    }

    private string RollContainer()
    {
        var containerRoll = dice.Roll(DiceExpr.D6);
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

    private (string GearDescription, Possessions.Scrolls.ScrollSchool? ScrollSchool, bool IsShield, int Quantity)
        RollGearSlot1(
            Abilities.Abilities abilities)
    {
        var d = dice.Roll(DiceExpr.D12);
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

    private (string GearDescription, Possessions.Scrolls.ScrollSchool? ScrollSchool, bool IsShield, int Quantity)
        RollGearSlot2()
    {
        var d = dice.Roll(DiceExpr.D12);
        return d switch
        {
            1 => ("life elixir", null, false, dice.Roll(DiceExpr.D4)),
            2 => ("random sacred scroll", ScrollSchool: ScrollSchool.Sacred, false, 1),
            3 => ("small but vicious dog", null, false, 1),
            4 => ("monkeys that ignore but love you", null, false, dice.Roll(DiceExpr.D4)),
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

    /// <summary>Beginning able to cast costs gear: the weapon table is capped at d6 and armour at d2,
    /// whatever the class would otherwise roll. A class already at or below the cap keeps its own die.</summary>
    private static DiceExpr CapForScroll(DiceExpr classDie, DiceExpr cap, bool hasScroll)
    {
        return hasScroll && classDie.Sides > cap.Sides ? cap : classDie;
    }

    private ScrollSchool RollScrollSchool()
    {
        return dice.Roll(DiceExpr.D2) == 1 ? ScrollSchool.Sacred : ScrollSchool.Unclean;
    }

    private Weapon RollWeapon(DiceExpr weaponDie)
    {
        // The table is ordered worst-first, so a smaller die is a worse kit: d10 for a classless scum or
        // a deserter, down to d4 for a hermit who can barely lift anything.
        var d = dice.Roll(weaponDie);
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

    private Armor RollArmor(DiceExpr armorDie)
    {
        var d = dice.Roll(armorDie);
        ArmorTier tier = d switch
        {
            1 => ArmorTier.None,
            2 => ArmorTier.Light,
            3 => ArmorTier.Medium,
            4 => ArmorTier.Heavy,
            _ => ArmorTier.None
        };
        return new Armor(tier);
    }

    private Abilities.Abilities RollAbilities(ClassSettings settings)
    {
        // Roll order is Agility, Presence, Strength, Toughness -- matching the Abilities constructor.
        return new Abilities.Abilities(
            RollToAbilityScoreMap(Roll(), settings.AgilityBonus),
            RollToAbilityScoreMap(Roll(), settings.PresenceBonus),
            RollToAbilityScoreMap(Roll(), settings.StrengthBonus),
            RollToAbilityScoreMap(Roll(), settings.ToughnessBonus)
        );

        AbilityScore RollToAbilityScoreMap(int sum, int classBonus)
        {
            // The bonus goes on the ROLL, not on the mapped modifier -- that is what "Strength +2" means,
            // and it is a far smaller edge than it looks: +2 on 3d6 shifts the modifier by one step at
            // most. It also makes the result unclampable, since the mapping never leaves -3..+3.
            return new AbilityScore(BaseModifier(sum + classBonus));
        }

        int BaseModifier(int sum)
        {
            return sum switch
            {
                <= 4 => -3,
                <= 6 => -2,
                <= 8 => -1,
                <= 12 => 0,
                <= 14 => +1,
                <= 16 => +2,
                _ => +3
            };
        }

        int Roll()
        {
            return dice.Roll(DiceExpr.D(3, 6));
        }
    }
}
