using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

public class AttackProfileDto
{
    [Description("Dice expression for weapon damage (e.g., '1d6', '2d4')")]
    public string DamageDice { get; set; } = string.Empty;

    [Description("Description of the weapon or attack method")]
    public string Description { get; set; } = string.Empty;
}
