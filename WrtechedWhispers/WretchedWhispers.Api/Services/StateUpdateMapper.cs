using WretchedWhispers.Api.Models;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;

namespace WretchedWhispers.Api.Services;

public static class StateUpdateMapper
{
    public static StateUpdate Map(SessionContext context)
    {
        var campaign = context.Campaign;
        var character = context.Character;

        int? characterHp = null;
        int? characterMaxHp = null;
        Guid? characterId = null;
        string? characterName = null;
        int? characterStrength = null;
        int? characterAgility = null;
        int? characterPresence = null;
        int? characterToughness = null;
        string? characterWeapon = null;
        string? characterArmor = null;
        string[]? characterInventory = null;
        int? characterSilver = null;
        bool hasLostEye = false;
        bool hasStabbedLung = false;
        bool hasBrokenHand = false;
        bool hasCrushedFoot = false;
        bool hasSeveredArm = false;
        bool hasSmashedFace = false;
        bool isInfected = false;
        bool isDizzyFromMagic = false;
        bool isEncumbered = false;
        bool isDead = false;
        string armorTier = "none";
        bool hasShield = false;
        bool isShieldBroken = false;

        if (character is not null)
        {
            characterId = character.Id;
            characterHp = character.Hp.Current;
            characterMaxHp = character.Hp.Max;
            characterName = character.Name;
            characterStrength = character.Abilities.Strength.Modifier;
            characterAgility = character.Abilities.Agility.Modifier;
            characterPresence = character.Abilities.Presence.Modifier;
            characterToughness = character.Abilities.Toughness.Modifier;
            characterWeapon = character.Weapon.Kind.ToString();
            characterArmor = character.Armor.Tier.DisplayName();
            characterInventory = character.Inventory.InventoryItems
                .Select(i => i.Description).ToArray();
            characterSilver = character.Silver;
            hasLostEye = character.HasLostEye;
            hasStabbedLung = character.HasStabbedLung;
            hasBrokenHand = character.HasBrokenHand;
            hasCrushedFoot = character.HasCrushedFoot;
            hasSeveredArm = character.HasSeveredArm;
            hasSmashedFace = character.HasSmashedFace;
            isInfected = character.IsInfected;
            isDizzyFromMagic = character.IsDizzyFromMagic;
            isEncumbered = character.IsEncumbered;
            isDead = character.IsDead;
            armorTier = character.Armor.Tier.Token();
            hasShield = character.Shield is not null;
            isShieldBroken = character.Shield?.IsBroken ?? false;
        }

        var stage = context.DeriveStage().ToString().ToLowerInvariant();

        string status;
        if (campaign is null || campaign.Players.Count == 0)
            status = "character-creation";
        else if (campaign.IsEnded || campaign.WorldEnded)
            status = "ended";
        else if (campaign.IsActive())
            status = "in-progress";
        else
            status = "in-progress"; // has players, campaign not started yet (setup phase)

        return new StateUpdate(
            CampaignId: campaign?.Id,
            CurrentDay: campaign?.CurrentDay ?? 0,
            CurrentHour: campaign?.CurrentHour ?? 0,
            CharacterId: characterId,
            CharacterName: characterName,
            CharacterHp: characterHp,
            CharacterMaxHp: characterMaxHp,
            CharacterStrength: characterStrength,
            CharacterAgility: characterAgility,
            CharacterPresence: characterPresence,
            CharacterToughness: characterToughness,
            CharacterWeapon: characterWeapon,
            CharacterArmor: characterArmor,
            CharacterInventory: characterInventory,
            CharacterSilver: characterSilver,
            MiseryCount: campaign?.Miseries.Count ?? 0,
            Stage: stage,
            Status: status,
            HasLostEye: hasLostEye,
            HasStabbedLung: hasStabbedLung,
            HasBrokenHand: hasBrokenHand,
            HasCrushedFoot: hasCrushedFoot,
            HasSeveredArm: hasSeveredArm,
            HasSmashedFace: hasSmashedFace,
            IsInfected: isInfected,
            IsDizzyFromMagic: isDizzyFromMagic,
            IsEncumbered: isEncumbered,
            IsDead: isDead,
            ArmorTier: armorTier,
            HasShield: hasShield,
            IsShieldBroken: isShieldBroken,
            WorldEnded: campaign?.WorldEnded ?? false);
    }
}
