using WretchedWhispers.Core.Characters.Abilities;

namespace WretchedWhispers.Core.Characters.Posessions;

public record Inventory(string Container, int MaxCapacity, List<InventoryItem> InventoryItems)
{
    public bool IsFull => GetFreeSlots() == 0;

    public void AddItem(InventoryItem item)
    {
        if (IsFull) throw new InvalidOperationException("Inventory is full, throw away another item to add a new one.");

        InventoryItems.Add(item);
    }

    public void RemoveItem(Guid itemId)
    {
        var item = InventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        InventoryItems.Remove(item);
    }

    public bool ConsumeItem(Guid itemId)
    {
        var item = InventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        if (!item.TryUseOne()) return false;

        if (item.Quantity == 0) InventoryItems.Remove(item);

        return true;
    }

    public void ReplenishItem(Guid itemId, int amount = 1)
    {
        var item = InventoryItems.FirstOrDefault(i => i.Id == itemId);

        if (item is null) throw new InvalidOperationException($"Item with id {itemId} is not in the inventory");

        item.Add(amount);
    }

    public int GetFreeSlots()
    {
        return MaxCapacity - CalculateOccupiedSlots();
    }

    private int CalculateOccupiedSlots()
    {
        return InventoryItems.Select(i => i.IsBulky ? 2 : 1).Sum();
    }

    public bool IsEncumbered(AbilityScore abilitiesStrength)
    {
        return abilitiesStrength.Modifier + 8 >= CalculateOccupiedSlots();
    }
}