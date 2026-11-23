using System.ComponentModel;

namespace WretchedWhispers.Engine.GameTools.Models;

[Description("What using one unit of an item did. Anything healed or cured here has ALREADY been applied.")]
public sealed record ItemUseOutcomeDto(
    [property: Description("Hit points the item restored; 0 for an item that does not heal")] int HpHealed,
    [property: Description("True when the item cleared an infection")] bool InfectionCured,
    [property: Description("The character after the item was used")] CharacterDto Character);
