using WretchedWhispers.Engine.Models;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Classes;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

namespace WretchedWhispers.Engine.Services;

public static class StateUpdateMapper
{
    public static StateUpdate Map(SessionContext context)
    {
        var campaign = context.Campaign;
        var character = context.Character;

        return new StateUpdate(
            CampaignId: campaign?.Id,
            CurrentDay: campaign?.CurrentDay ?? 0,
            CurrentHour: campaign?.CurrentHour ?? 0,
            CharacterId: character?.Id,
            CharacterName: character?.Name,
            CharacterHp: character?.Hp.Current,
            CharacterMaxHp: character?.Hp.Max,
            CharacterStrength: character?.Abilities.Strength.Modifier,
            CharacterAgility: character?.Abilities.Agility.Modifier,
            CharacterPresence: character?.Abilities.Presence.Modifier,
            CharacterToughness: character?.Abilities.Toughness.Modifier,
            CharacterWeapon: character?.Weapon.Kind.ToString(),
            CharacterArmor: character?.Armor.Tier.DisplayName(),
            // One entry per UNIT: the UI groups duplicates back with a xN badge, and the turn-delta
            // multiset diff needs units so a quantity decrement (3 torches -> 2) surfaces as one
            // removed entry instead of vanishing (per-item mapping hid all quantity-only changes).
            CharacterInventory: character?.Inventory.InventoryItems
                .SelectMany(i => Enumerable.Repeat(i.Description, i.Quantity)).ToArray(),
            CharacterSilver: character?.Silver,
            MiseryCount: campaign?.Miseries.Count ?? 0,
            Stage: context.DeriveStage().ToString().ToLowerInvariant(),
            Status: context.DeriveStatus(),
            HasLostEye: character?.HasLostEye ?? false,
            HasStabbedLung: character?.HasStabbedLung ?? false,
            HasBrokenHand: character?.HasBrokenHand ?? false,
            HasCrushedFoot: character?.HasCrushedFoot ?? false,
            HasSeveredArm: character?.HasSeveredArm ?? false,
            HasSmashedFace: character?.HasSmashedFace ?? false,
            IsInfected: character?.IsInfected ?? false,
            IsDizzyFromMagic: character?.IsDizzyFromMagic ?? false,
            IsEncumbered: character?.IsEncumbered ?? false,
            IsDead: character?.IsDead ?? false,
            ArmorTier: character?.Armor.Tier.Token() ?? "none",
            HasShield: character?.Shield is not null,
            IsShieldBroken: character?.Shield?.IsBroken ?? false,
            WorldEnded: campaign?.WorldEnded ?? false,
            CurrentLocationName: campaign?.CurrentLocationName,
            CharacterOmens: character?.Omens.Count,
            CharacterScrolls: character?.Scrolls
                .Select(s => $"{s.Description} ({s.School})").ToArray(),
            MiseryPsalms: campaign?.Miseries
                .Select(m => string.IsNullOrEmpty(m.Psalm) ? m.Code : m.Psalm).ToArray() ?? [],
            CharacterClass: DisplayClass(character));
    }

    /// <summary>Null for classless wretches, so the UI shows a class line only when there is one.</summary>
    private static string? DisplayClass(Character? character) =>
        character is null || character.Class == CharacterClass.Classless
            ? null
            : ClassPresets.For(character.Class).DisplayName;
}
