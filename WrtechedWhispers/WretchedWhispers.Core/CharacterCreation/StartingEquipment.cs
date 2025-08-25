using WretchedWhispers.Core.Characters.Inventory;
using WretchedWhispers.Core.Characters.Inventory.Armor;
using WretchedWhispers.Core.Characters.Inventory.Weapon;
using WretchedWhispers.Core.Scrolls;

namespace WretchedWhispers.Core.CharacterCreation;

public readonly record struct StartingEquipment(
    int Silver,
    int FoodDays,
    string Container,
    string Gear1,
    string Gear2,
    Weapon Weapon,
    Armor Armor,
    Shield? Shield,
    List<Scroll> Scrolls);