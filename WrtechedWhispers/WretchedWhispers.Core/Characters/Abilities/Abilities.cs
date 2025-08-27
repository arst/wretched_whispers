namespace WretchedWhispers.Core.Characters.Abilities;

public sealed class Abilities(AbilityScore agi, AbilityScore pre, AbilityScore str, AbilityScore tou)
{
    public AbilityScore Agility { get; private set; } = agi;
    public AbilityScore Presence { get; private set; } = pre;
    public AbilityScore Strength { get; private set; } = str;
    public AbilityScore Toughness { get; private set; } = tou;

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