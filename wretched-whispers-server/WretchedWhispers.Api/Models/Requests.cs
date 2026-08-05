using System.Diagnostics.CodeAnalysis;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters.Classes;

namespace WretchedWhispers.Api.Models;

/// <summary>
/// Everything the player chooses up front. Name, class and difficulty are all decisions, so they come
/// from the form rather than being negotiated with the narrator — only the dice-rolled parts
/// (abilities, gear, HP, omens) are left to the domain.
/// </summary>
/// <param name="CharacterClass">
/// Null means the player asked the dice to decide, and the domain rolls one of the six. Pass
/// <see cref="Core.Characters.Classes.CharacterClass.Classless"/> only when they chose to be classless scum.
/// </param>
public sealed record CreateSessionRequest(
    string CharacterName = "",
    Difficulty Difficulty = Difficulty.Grim,
    CharacterClass? CharacterClass = null);

/// <summary>
/// A replacement wretch for a campaign whose last one died. No difficulty: that belongs to the
/// campaign and a death does not renegotiate it.
/// </summary>
/// <param name="CharacterClass">
/// Null means the player asked the dice to decide, and the domain rolls one of the six.
/// </param>
public sealed record CreateSuccessorRequest(
    string CharacterName = "",
    CharacterClass? CharacterClass = null);

public sealed record SubmitTurnRequest(Guid RequestId, string Message);

/// <summary>
/// The two free-text fields the player controls. Both reach the database and the narrator's prompt,
/// so both are normalised and bounded here at the trust boundary rather than downstream — and the
/// limits are stated once, so the error text can't drift from the rule it describes.
/// </summary>
public static class PlayerInput
{
    public const int MaxCharacterNameLength = 64;
    public const int MaxMessageLength = 4000;

    public static bool TryCharacterName(string? raw, [NotNullWhen(true)] out string? name, out string error)
    {
        name = raw?.Trim() ?? "";
        if (name.Length == 0)
            return Reject("A wretch needs a name.", out name, out error);

        if (name.Length > MaxCharacterNameLength)
            return Reject(
                $"That name is too long; keep it under {MaxCharacterNameLength} characters.", out name, out error);

        // Newlines and other control characters would let a name forge turns of its own once it is
        // interpolated into the narrator's prompt.
        if (name.Any(char.IsControl))
            return Reject("A name cannot contain line breaks or control characters.", out name, out error);

        error = "";
        return true;
    }

    public static bool TryTurnMessage(string? raw, [NotNullWhen(true)] out string? message, out string error)
    {
        message = raw?.Trim() ?? "";
        if (message.Length == 0)
            return Reject("Say something.", out message, out error);

        if (message.Length > MaxMessageLength)
            return Reject(
                $"That is too long; keep it under {MaxMessageLength} characters.", out message, out error);

        error = "";
        return true;
    }

    private static bool Reject(string reason, out string? value, out string error)
    {
        value = null;
        error = reason;
        return false;
    }
}
