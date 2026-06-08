using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.GameTools.Models;

public class InventoryItemDto
{
    [JsonPropertyName("Id")]
    [Description("Unique identifier for the inventory item")]
    public Guid Id { get; set; }

    [JsonPropertyName("Description")]
    [Description("Description of the item")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("IsBulky")]
    [Description("Whether the item takes up extra inventory space")]
    public bool IsBulky { get; set; }

    [JsonPropertyName("IsOneTimeUse")]
    [Description("Whether the item is consumed after one use")]
    public bool IsOneTimeUse { get; set; }

    [JsonPropertyName("Quantity")]
    [Description("Number of items of this type in the stack")]
    public int Quantity { get; set; }
}