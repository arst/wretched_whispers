using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Weapons;
using WretchedWhispers.Core.Dices;

namespace WretchedWhispers.Tests;

/// <summary>
/// Shared minimal character factory for tests that need a valid Character without exercising
/// CharacterCreationService's randomness. Mirrors the ability-per-parameter idiom already used by
/// AttackHitRateTests.CreateHero.
/// </summary>
public static class TestCharacters
{
    public static Character Create(Dice dice, int agility = 0, int presence = 0, int strength = 0,
        int toughness = 0)
    {
        var abilities = new Abilities(
            agility: new AbilityScore(agility),
            presence: new AbilityScore(presence),
            strength: new AbilityScore(strength),
            toughness: new AbilityScore(toughness));
        var equipment = new StartingEquipment(
            Silver: 10, FoodDays: 3, Container: "backpack (7 items)",
            Gear1: null, Gear2: null,
            Weapon: Weapon.Create(WeaponKind.Sword),
            Armor: new Armor(ArmorTier.None),
            Shield: null, Scrolls: []);
        return Character.Create(Guid.NewGuid(), "TestHero", 20, abilities, equipment, dice);
    }
}
