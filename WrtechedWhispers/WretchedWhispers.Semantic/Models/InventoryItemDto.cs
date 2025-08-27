namespace WretchedWhispers.Semantic.Models;

public class InventoryItemDto
{
    public Guid Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsBulky { get; set; }

    public bool IsOneTimeUse { get; set; }

    public int Quantity { get; set; }
}