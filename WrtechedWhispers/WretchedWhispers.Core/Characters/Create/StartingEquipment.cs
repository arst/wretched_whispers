using WretchedWhispers.Core.Characters.Possessions;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Possessions.Weapons;

namespace WretchedWhispers.Core.Characters.Create;

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