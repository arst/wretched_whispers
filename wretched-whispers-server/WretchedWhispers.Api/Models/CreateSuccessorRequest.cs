using WretchedWhispers.Core.Characters.Classes;

namespace WretchedWhispers.Api.Models;

/// <summary>
/// A replacement wretch for a campaign whose last one died. No difficulty: that belongs to the campaign and
/// a death does not renegotiate it.
/// </summary>
/// <param name="CharacterClass">
/// Null means the player asked the dice to decide, and the domain rolls one of the six.
/// </param>
public record CreateSuccessorRequest(
    string CharacterName = "",
    CharacterClass? CharacterClass = null);
