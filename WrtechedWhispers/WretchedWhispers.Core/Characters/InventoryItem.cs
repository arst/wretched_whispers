namespace WretchedWhispers.Core.Characters;

public class InventoryItem(Guid id, string description, bool isBulky, bool isOneTimeUse, int quantity = 1)
{
    public Guid Id { get; } = id;

    public string Description { get; } = description;

    public bool IsBulky { get; } = isBulky;

    public int Quantity { get; private set; } = quantity;

    public bool IsOneTimeUse { get; } = isOneTimeUse;

    public void Add(int amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount to add must be positive.");
        Quantity += amount;
    }

    public bool TryUseOne()
    {
        if (Quantity <= 0) return false;
        Quantity--;
        return true;
    }
}