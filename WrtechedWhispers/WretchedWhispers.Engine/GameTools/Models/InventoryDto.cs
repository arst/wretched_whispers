using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Engine.GameTools.Models;

public class InventoryDto
{
    [JsonPropertyName("Container")]
    [Description("Name or type of container holding the items (e.g., 'Sack', 'Backpack')")]
    public string Container { get; set; } = string.Empty;

    [JsonPropertyName("MaxCapacity")]
    [Description("Maximum number of items the inventory can hold")]
    public int MaxCapacity { get; set; }

    [JsonPropertyName("FreeSlots")]
    [Description("Number of free slots remaining in the inventory")]
    public int FreeSlots { get; set; }

    [JsonPropertyName("IsFull")]
    [Description("Whether the inventory is at maximum capacity")]
    public bool IsFull { get; set; }

    [JsonPropertyName("Items")]
    [Description("List of items currently in the inventory")]
    public List<InventoryItemDto> Items { get; set; } = [];
}