using System.Text.Json.Serialization;

namespace WretchedWhispers.Core.Characters.Abilities;

public sealed class Abilities
{
    [JsonConstructor]
    public Abilities(AbilityScore agility, AbilityScore presence, AbilityScore strength, AbilityScore toughness)
    {
        Agility = agility;
        Presence = presence;
        Strength = strength;
        Toughness = toughness;
    }

    [JsonInclude] public AbilityScore Agility { get; private set; }
    [JsonInclude] public AbilityScore Presence { get; private set; }
    [JsonInclude] public AbilityScore Strength { get; private set; }
    [JsonInclude] public AbilityScore Toughness { get; private set; }

    public AbilityScore this[AbilityKind kind] => kind switch
    {
        AbilityKind.Agility => Agility,
        AbilityKind.Presence => Presence,
        AbilityKind.Strength => Strength,
        AbilityKind.Toughness => Toughness,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public void ModifyAbility(AbilityKind kind, int delta)
    {
        switch (kind)
        {
            case AbilityKind.Agility:
                Agility = new AbilityScore(Agility.Modifier + delta);
                break;
            case AbilityKind.Presence:
                Presence = new AbilityScore(Presence.Modifier + delta);
                break;
            case AbilityKind.Strength:
                Strength = new AbilityScore(Strength.Modifier + delta);
                break;
            case AbilityKind.Toughness:
                Toughness = new AbilityScore(Toughness.Modifier + delta);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}