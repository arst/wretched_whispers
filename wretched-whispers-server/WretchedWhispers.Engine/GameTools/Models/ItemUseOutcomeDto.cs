using System.ComponentModel;
using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Status;

namespace WretchedWhispers.Engine.GameTools.Models;

[Description("What using one unit of an item did. Anything healed, cured or suffered here has ALREADY been applied.")]
public sealed record ItemUseOutcomeDto(
    [property: Description("Hit points the item restored; 0 for an item that does not heal")] int HpHealed,
    [property: Description("True when the item cleared an infection")] bool InfectionCured,
    [property: Description("The character after the item was used")] CharacterDto Character,
    [property: Description("Damage the item did to the character, after any test against it")] int DamageTaken = 0,
    [property: Description("The injury the Broken table gave, when the damage dropped the character to 0")] string? NewInjury = null,
    [property: Description("The Toughness test a swallowed poison called for, as roll+modifier against DR")] string? PoisonTest = null)
{
    /// <summary>Dead or broken, in words, as every result that can kill says it. See <see cref="FateWords"/>.</summary>
    [JsonPropertyOrder(-1)]
    public string? Fate => FateWords.For(Character.IsDead, NewInjury, Character.CurrentHp);

    /// <summary>What the item did to the character, in words. In the fifth simulated run the death-seeker drank
    /// a poison that did nothing (hpHealed 0), and the narrator told "FATE: DEAD." with no result saying so; the
    /// game was stuck for three turns while the server refused a successor.</summary>
    [JsonPropertyOrder(-1)]
    public string Effect =>
        DamageTaken > 0
            ? $"It harmed the character: {DamageTaken} damage{Because}. Hit points now {Character.CurrentHp} of {Character.MaxHp}."
            : HpHealed > 0 || InfectionCured
                ? $"It healed {HpHealed} hit points{(InfectionCured ? " and cleared the infection" : "")}. Hit points now "
                  + $"{Character.CurrentHp} of {Character.MaxHp}."
                : $"It did nothing to the character's body{Because}: they are alive at {Character.CurrentHp} of "
                  + $"{Character.MaxHp} hit points. Nothing else changes because of the item itself.";

    private string Because => PoisonTest is null ? "" : $" (poison, Toughness test {PoisonTest})";
}
