using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public class InventoryItemDto
{
    [Description("Unique identifier for the inventory item")]
    public Guid Id { get; set; }

    [Description("Description of the item")]
    public string Description { get; set; } = string.Empty;

    [Description("Whether the item takes up extra inventory space")]
    public bool IsBulky { get; set; }

    [Description("Whether the item is consumed after one use")]
    public bool IsOneTimeUse { get; set; }

    [Description("Number of items of this type in the stack")]
    public int Quantity { get; set; }
}