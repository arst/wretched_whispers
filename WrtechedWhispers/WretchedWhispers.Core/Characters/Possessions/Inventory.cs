using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Abilities;

namespace WretchedWhispers.Core.Characters.Possessions;

public sealed class Inventory
{
    private readonly List<InventoryItem> _inventoryItems;

    [JsonConstructor]
    public Inventory(string container, int maxCapacity, List<InventoryItem> inventoryItems)
    {
        Container = container;
        MaxCapacity = maxCapacity;
        _inventoryItems = inventoryItems ?? [];
    }

    public string Container { get; }
    [JsonInclude] public int MaxCapacity { get; internal set; }

    /// <summary>
    ///     The items in the inventory. Use AddItem/RemoveItem/ConsumeItem/ReplenishItem to mutate.
    ///     Typed as List for STJ constructor parameter binding compatibility.
    /// </summary>
    public List<InventoryItem> InventoryItems => _inventoryItems;

    [JsonIgnore] public bool IsFull => GetFreeSlots() == 0;

    public void AddItem(InventoryItem item)
    {
        if (IsFull) throw new InvalidOperationException("Inventory is full, throw away another item to add a new one.");

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
        return abilitiesStrength.Modifier + 8 <= CalculateOccupiedSlots();
    }
}
