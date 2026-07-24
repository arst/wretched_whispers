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

    public AbilityScore Agility { get; }
    public AbilityScore Presence { get; }
    public AbilityScore Strength { get; }
    public AbilityScore Toughness { get; }

    public AbilityScore this[AbilityKind kind] => kind switch
    {
        AbilityKind.Agility => Agility,
        AbilityKind.Presence => Presence,
        AbilityKind.Strength => Strength,
        AbilityKind.Toughness => Toughness,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public Abilities ModifyAbility(AbilityKind kind, int delta)
    {
        return kind switch
        {
            AbilityKind.Agility => new Abilities(new AbilityScore(Agility.Modifier + delta), Presence, Strength, Toughness),
            AbilityKind.Presence => new Abilities(Agility, new AbilityScore(Presence.Modifier + delta), Strength, Toughness),
            AbilityKind.Strength => new Abilities(Agility, Presence, new AbilityScore(Strength.Modifier + delta), Toughness),
            AbilityKind.Toughness => new Abilities(Agility, Presence, Strength, new AbilityScore(Toughness.Modifier + delta)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}
