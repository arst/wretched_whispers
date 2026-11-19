using System.Text.RegularExpressions;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Last-line defence for player-facing narration: strips raw entity GUIDs the model should never
/// surface (the persona forbids it, but this enforces it structurally). Conservative on purpose —
/// it only removes GUIDs, leaving all legitimate prose untouched.
/// </summary>
public static partial class OutputScrubber
{
    [GeneratedRegex(
        @"\(?\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b\)?")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"[ \t]{2,}")]
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

/// <summary>
/// <see cref="OutputScrubber"/> for text that arrives in pieces and is shown as it arrives. A GUID can
/// be split across streamed pieces, so the tail of the text that could still turn out to be the start
/// of one is held back until more arrives. Text is released in pieces of at least
/// <see cref="MinChunk"/> characters, so a narration costs a handful of events, not one per token.
/// </summary>
public sealed class StreamingScrubber
{
    private const int MinChunk = 32;
    private const int GuidLength = 36;

    private string _pending = string.Empty;

    /// <summary>True once any GUID has been removed.</summary>
    public bool Redacted { get; private set; }

    /// <summary>Adds text; returns what is now safe to show (possibly empty).</summary>
    public string Append(string text)
    {
        _pending += text;
        var release = _pending.Length - HeldBack(_pending);
        if (release < MinChunk) return string.Empty;

        var released = Clean(_pending[..release]);
        _pending = _pending[release..];
        return released;
    }

    /// <summary>Returns everything still held. Call when no more text can follow it directly.</summary>
    public string Flush()
    {
        var rest = Clean(_pending);
        _pending = string.Empty;
        return rest;
    }

    // Removing a GUID from the very end of a piece leaves the blank that preceded it; the blank that
    // followed it opens the next piece. Scrubbed whole, the two would have been collapsed into one.
    private bool _dropLeadingBlanks;

    private string Clean(string text)
    {
        var clean = OutputScrubber.RedactGuids(text, out var redacted);
        Redacted |= redacted;

        if (_dropLeadingBlanks && clean.Length > 0)
        {
            clean = clean.TrimStart(' ', '\t');
            _dropLeadingBlanks = false;
        }
        if (redacted && clean.Length > 0 && clean[^1] is ' ' or '\t')
            _dropLeadingBlanks = true;
        return clean;
    }

    // How much of the end of the (still raw) text must wait, so that scrubbing the released part and
    // the rest separately gives the same result as scrubbing the whole: an unfinished — or finished
    // but possibly still to be closed by ")" — run of hex digits and dashes, an opening "(" or
    // "(id: " right before it, and the blanks around them (the tidy-up collapses runs of blanks).
    private static int HeldBack(string text)
    {
        static bool IsBlank(char c) => c is ' ' or '\t';
        static bool IsGuidChar(char c) => Uri.IsHexDigit(c) || c == '-';

        var end = text.Length;
        while (end > 0 && IsBlank(text[end - 1])) end--;

        var start = end;
        while (start > 0 && end - start < GuidLength && IsGuidChar(text[start - 1]))
            start--;
        // A run longer than a GUID is not one in the making.
        if (end - start == GuidLength && start > 0 && IsGuidChar(text[start - 1])) start = end;

        for (var i = start - 1; i >= 0 && start - i <= 6; i--)
        {
            if (text[i] == '(') { start = i; break; }
            if (!"idID: ".Contains(text[i])) break;
        }

        while (start > 0 && IsBlank(text[start - 1])) start--;
        return text.Length - start;
    }
}
