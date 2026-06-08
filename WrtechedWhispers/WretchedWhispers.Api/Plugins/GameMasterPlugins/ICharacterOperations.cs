using WretchedWhispers.Core.Characters.Abilities;
using WretchedWhispers.Api.GameTools.Models;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// Contract for character operations that wrapper plugins delegate to.
/// Implemented by CharacterPlugin via an adapter.
/// </summary>
public interface ICharacterOperations
{
    Task<CharacterDto> CreateCharacter(string name);
    Task<ChallengeOutcomeDto> ChallengeCharacter(Guid characterId, int challengeDr, AbilityKind abilityKind);
    Task<CharacterDto> AddItemToCharacterInventory(Guid characterId, string itemDescription, bool isBulky, bool isOneTimeUse, int quantity);
    Task<CharacterDto> RemoveItemFromCharacterInventory(Guid characterId, Guid itemId);
    Task<CharacterDto> ImproveCharacterAbility(Guid characterId, AbilityKind abilityKind, int delta);
    Task<CharacterDto> DegradeCharacterAbility(Guid characterId, AbilityKind abilityKind, int delta);
    Task<CharacterDto> InfectCharacter(Guid characterId);
    Task<CharacterDto> CureInfection(Guid characterId);
    Task<CharacterDto> BuyItem(Guid characterId, string itemDescription, int silverCost, bool isBulky, bool isOneTimeUse, int quantity);
    Task<CastOutcomeDto> CastScroll(Guid characterId, Guid scrollId);
}
