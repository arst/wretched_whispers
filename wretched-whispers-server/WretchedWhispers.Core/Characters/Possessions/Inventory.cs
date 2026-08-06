using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Abilities;

namespace WretchedWhispers.Core.Characters.Possessions;

public sealed class Inventory
{
    private readonly List<InventoryItem> _inventoryItems;

    // Param type must equal the property type or STJ refuses to bind the constructor.
    [JsonConstructor]
    public Inventory(string container, int maxCapacity, IReadOnlyList<InventoryItem> inventoryItems)
    {
        Container = container;
        MaxCapacity = maxCapacity;
        _inventoryItems = inventoryItems?.ToList() ?? [];
    }

    public string Container { get; }
    [JsonInclude] public int MaxCapacity { get; internal set; }

    /// <summary>Read-only projection over the list the constructor binds — mutation goes through
    /// AddItem/RemoveItem/ConsumeItem/ReplenishItem.</summary>
    public IReadOnlyList<InventoryItem> InventoryItems => _inventoryItems;

    // <= not ==: a Strength loss can shrink MaxCapacity below what's already carried, driving free
    // slots negative — an equality test would report that overstuffed inventory as not-full forever.
    [JsonIgnore] public bool IsFull => GetFreeSlots() <= 0;

    /// <summary>The one home of the carry rule: Strength+8 slots carried free, twice that as the hard cap.</summary>
    public static int CapacityFor(AbilityScore strength)
    {
        return 2 * FreeCarrySlots(strength);
    }

    private static int FreeCarrySlots(AbilityScore strength)
    {
        return strength.Modifier + 8;
    }

    public void AddItem(InventoryItem item)
    {
        // The item's whole slot cost must fit: a bulky item needs two slots, and one free slot
        // plus a bulky add would push occupancy past capacity.
        if (GetFreeSlots() < (item.IsBulky ? 2 : 1))
            throw new InvalidOperationException("Inventory is full, throw away another item to add a new one.");

        _inventoryItems.Add(item);
    }

    public void RemoveItem(Guid itemId)
    {
        var item = _inventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        _inventoryItems.Remove(item);
    }

    public bool ConsumeItem(Guid itemId)
    {
        var item = _inventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        if (!item.TryUseOne()) return false;

        if (item.Quantity == 0) _inventoryItems.Remove(item);

        return true;
    }

    public void ReplenishItem(Guid itemId, int amount = 1)
    {
        var item = _inventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        item.Add(amount);
    }

    public int GetFreeSlots()
    {
        return MaxCapacity - CalculateOccupiedSlots();
    }

    private int CalculateOccupiedSlots()
    {
        return _inventoryItems.Select(i => i.IsBulky ? 2 : 1).Sum();
    }

    public bool IsEncumbered(AbilityScore abilitiesStrength)
    {
        // Strictly above the free allowance: exactly Strength+8 slots is still carried free.
        return CalculateOccupiedSlots() > FreeCarrySlots(abilitiesStrength);
    }
}
