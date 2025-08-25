using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Core.Abilities;
using WretchedWhispers.Core.CharacterCreation;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Inventory.Armor.Tiers;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Services;
using WretchedWhispers.Semantic.Models;

namespace WretchedWhispers.Semantic;

[Description(
    "Allow interacting with characters, such as creating a new character or viewing character stats, performing actions on characters, and more.")]
public sealed class CharacterPlugin(
    ICharactersRepository charactersRepository,
    ICharacterCreationService characterCreationService,
    CharacterService characterService)
{
    [KernelFunction]
    [Description("Create a new character with starting stats and gear")]
    public async Task<CharacterDto> CreateCharacter([Description("Character name")] string name)
    {
        var character = await characterCreationService.Create(name);
        await charactersRepository.Save(character);

        return new CharacterDto
        {
            Id = character.Id,
            Name = character.Name,
            Silver = character.Silver,
            FoodDays = character.FoodDays,
            CurrentHp = character.Hp.Current,
            MaxHp = character.Hp.Max,
            Strength = character.Abilities.Strength.Modifier,
            Agility = character.Abilities.Agility.Modifier,
            Presence = character.Abilities.Presence.Modifier,
            Toughness = character.Abilities.Toughness.Modifier,
            WeaponKind = character.Weapon.Kind,
            ArmorTier = GetArmorTier(character.Armor.Tier),
            HasShield = character.Shield is not null,
            IsShieldBroken = character.Shield?.IsBroken ?? false,
            OmenCount = character.Omens.Count,
            PowersMax = character.Powers.MaxUses,
            PowersUsed = character.Powers.UsesRemaining,
            IsInfected = character.IsInfected,
            IsDizzyFromMagic = character.IsDizzyFromMagic,
            KnownScrolls = character.KnownScrolls.Select(s => new ScrollDto
            {
                Key = s.Key,
                School = s.School
            }).ToList(),
            Container = character.Gear.Container,
            Gear1 = character.Gear.Gear1,
            Gear2 = character.Gear.Gear2
        };
    }

    [KernelFunction]
    [Description("Challenge a character with an ability test against a specified difficulty rating")]
    public async Task<ChallengeOutcomeDto> ChallengeCharacter(
        [Description("Id of the character to challenge")]
        Guid characterId,
        [Description(
            "Level of the challenge, the higher the number the harder the challenge. Usually equal to 12 for a normal challenge.")]
        int challengeDr,
        [Description("Ability kind to use for the challenge, one of: 'Strength', 'Agility', 'Presence', 'Toughness'.")]
        AbilityKind abilityKind)
    {
        var outcome = await characterService.ChallengePlayer(characterId, new Dr(challengeDr), abilityKind);
        return new ChallengeOutcomeDto(outcome.IsSuccess);
    }

    private static ArmorTierDto GetArmorTier(ArmorTier armorTier)
    {
        return armorTier switch
        {
            HeavyArmorTier => ArmorTierDto.Heavy,
            NoArmorTier => ArmorTierDto.None,
            LightArmorTier => ArmorTierDto.Light,
            MediumArmorTier => ArmorTierDto.Medium,
            _ => throw new ArgumentOutOfRangeException(nameof(armorTier), armorTier, null)
        };
    }
}