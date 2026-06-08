using System.ComponentModel;
using System.Text.Json.Serialization;

namespace WretchedWhispers.Api.GameTools.Models;

public record AdversaryAttackOutcomeDto(
    [property: JsonPropertyName("DamageDealt")]
    [property: Description("Amount of damage dealt by the adversary's attack")]
    int DamageDealt);