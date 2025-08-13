using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Armor;
using WretchedWhispers.Core.Characters.Weapon;
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