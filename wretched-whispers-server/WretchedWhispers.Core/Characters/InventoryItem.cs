using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters;

public class InventoryItem
{
    [JsonConstructor]
    public InventoryItem(Guid id, string description, bool isBulky, bool isOneTimeUse, int quantity = 1)
    {
        Id = id;
        Description = description;
        IsBulky = isBulky;
        IsOneTimeUse = isOneTimeUse;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public string Description { get; }

    public bool IsBulky { get; }

    [JsonInclude] public int Quantity { get; private set; }

    public bool IsOneTimeUse { get; }

    public void Add(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Quantity += amount;
    }

    public bool TryUseOne()
    {
        if (Quantity <= 0) return false;
        Quantity--;
        return true;
    }
}
