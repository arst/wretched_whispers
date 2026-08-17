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
    List<Scroll> Scrolls,
    // Class kit, on top of the two rolled gear slots. Trailing and optional so the 20-odd existing
    // call sites in tests and evals keep compiling unchanged.
    List<InventoryItem>? ClassKit = null);
