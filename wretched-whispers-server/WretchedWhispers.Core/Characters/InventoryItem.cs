using System.Text.Json.Serialization;
using WretchedWhispers.Core.Characters.Challenge;
using WretchedWhispers.Core.Characters.Status;

namespace WretchedWhispers.Core.Characters;

/// <summary>What using an item does beyond spending it. Carried by the item, applied by
/// <see cref="Character.UseItem"/>.</summary>
public enum ItemEffect
{
    None,

    /// <summary>Restores d6 hit points and cures infection: the life elixir and the medicine chest.</summary>
    Heal,

    /// <summary>Red poison: whoever drinks it makes a Toughness DR12 test or takes d10 damage. In the fifth
    /// simulated run poison did nothing, and the narrator told a death no result gave.</summary>
    Poison
}

/// <summary>What using one unit of an item did to the character. <paramref name="PoisonTest"/> is the
/// Toughness test a swallowed poison called for; null when there was none.</summary>
public readonly record struct ItemUseOutcome(
    int HpHealed, bool InfectionCured, int Damage = 0, InjuryKind? Injury = null,
    ChallengeOutcome? PoisonTest = null);

public class InventoryItem
{
    [JsonConstructor]
    public InventoryItem(Guid id, string description, bool isBulky, bool isOneTimeUse, int quantity = 1,
        ItemEffect effect = ItemEffect.None)
    {
        Effect = effect;
        Id = id;
        Description = description;
        IsBulky = isBulky;
        IsOneTimeUse = isOneTimeUse;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public string Description { get; }

    public bool IsBulky { get; }

    [JsonInclude] public int Quantity { get; private set; }

    public bool IsOneTimeUse { get; }

    public ItemEffect Effect { get; }

    public void Add(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Quantity += amount;
    }

    public bool TryUseOne()
    {
        if (Quantity <= 0) return false;
        Quantity--;
        return true;
    }
}
