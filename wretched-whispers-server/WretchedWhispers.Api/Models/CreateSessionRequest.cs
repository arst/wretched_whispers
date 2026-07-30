using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Classes;

namespace WretchedWhispers.Api.Models;

/// <summary>
/// Everything the player chooses up front. Name, class and difficulty are all decisions, so they come from
/// the form rather than being negotiated with the narrator -- only the dice-rolled parts (abilities, gear,
/// HP, omens) are left to the domain.
/// </summary>
/// <param name="CharacterClass">
/// Null means the player asked the dice to decide, and the domain rolls one of the six. Pass
/// <see cref="Core.Characters.Classes.CharacterClass.Classless"/> only when they chose to be classless scum.
/// </param>
public record CreateSessionRequest(
    string CharacterName = "",
    Difficulty Difficulty = Difficulty.Grim,
    CharacterClass? CharacterClass = null);
