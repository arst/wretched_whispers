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
        ItemEffect effect = ItemEffect.None, int? uses = null)
    {
        if (uses is not null && (uses <= 0 || quantity != 1))
            throw new ArgumentException("An item with uses is one item (quantity 1) holding at least one use.",
                nameof(uses));
        Uses = uses;
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

    /// <summary>For one item spent a little at a time (a medicine chest, a flask of doses): the uses left in
    /// it. Null for an item used up whole, where <see cref="Quantity"/> counts the separate items. Kept apart
    /// so no count is written into a name: in the sixth simulated run "medicine chest (Presence + 4 uses) x4"
    /// was told as one chest, then three, then twelve uses.</summary>
    [JsonInclude] public int? Uses { get; private set; }

    public bool IsOneTimeUse { get; }

    public ItemEffect Effect { get; }

    public void Add(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Quantity += amount;
    }

    /// <summary>Spends one use of an item that has uses, or one of the items otherwise. The last use spends
    /// the item.</summary>
    public bool TryUseOne()
    {
        if (Quantity <= 0) return false;
        if (Uses is { } uses && (Uses = uses - 1) > 0) return true;
        Quantity--;
        return true;
    }

    /// <summary>Takes whole items away (sold, given, lost), whatever uses they hold.</summary>
    internal void TakeAway(int units) => Quantity -= units;
}
