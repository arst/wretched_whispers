using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Posessions;
using WretchedWhispers.Core.Characters.Posessions.Armor;
using WretchedWhispers.Core.Characters.Posessions.Scrolls;
using WretchedWhispers.Core.Characters.Posessions.Weapon;

namespace WretchedWhispers.Core.CharacterCreation;

public readonly record struct StartingEquipment(
    int Silver,
    int FoodDays,
    string Container,
    InventoryItem? Gear1,
    InventoryItem? Gear2,
    Weapon Weapon,
    Armor Armor,
    Shield? Shield,
    List<Scroll> Scrolls);