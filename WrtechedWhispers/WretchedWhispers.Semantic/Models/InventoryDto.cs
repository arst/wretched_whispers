namespace WretchedWhispers.Semantic.Models;

public class InventoryDto
{
    public string Container { get; set; } = string.Empty;
    public int MaxCapacity { get; set; }
    public int FreeSlots { get; set; }
    public bool IsFull { get; set; }
    public List<InventoryItemDto> Items { get; set; } = [];
}