using System.Text.RegularExpressions;

namespace WretchedWhispers.Api.Services;

/// <summary>
/// Last-line defence for player-facing narration: strips raw entity GUIDs the model should never
/// surface (the persona forbids it, but this enforces it structurally). Conservative on purpose —
/// it only removes GUIDs, leaving all legitimate prose untouched.
/// </summary>
public static partial class OutputScrubber
{
    [GeneratedRegex(
        @"\(?\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b\)?",
        RegexOptions.Compiled)]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex RepeatedSpaces();

    /// <summary>Returns the text with any GUIDs removed; out param reports whether anything changed.</summary>
    public static string RedactGuids(string text, out bool redacted)
    {
        if (string.IsNullOrEmpty(text) || !GuidPattern().IsMatch(text))
        {
            redacted = false;
            return text;
        }

        redacted = true;
        var stripped = GuidPattern().Replace(text, string.Empty);
        // Tidy up the gaps a removed "(guid)" or "id: guid" can leave behind.
        stripped = stripped.Replace("(id: )", "").Replace("(ID: )", "").Replace("()", "");
        return RepeatedSpaces().Replace(stripped, " ");
    }
}
