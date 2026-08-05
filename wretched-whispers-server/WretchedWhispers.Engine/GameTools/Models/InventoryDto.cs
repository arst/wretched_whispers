using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public class InventoryDto
{
    [Description("Name or type of container holding the items (e.g., 'Sack', 'Backpack')")]
    public string Container { get; set; } = string.Empty;

    [Description("Maximum number of items the inventory can hold")]
    public int MaxCapacity { get; set; }

    [Description("Number of free slots remaining in the inventory")]
    public int FreeSlots { get; set; }

    [Description("Whether the inventory is at maximum capacity")]
    public bool IsFull { get; set; }

    [Description("List of items currently in the inventory")]
    public List<InventoryItemDto> Items { get; set; } = [];
}