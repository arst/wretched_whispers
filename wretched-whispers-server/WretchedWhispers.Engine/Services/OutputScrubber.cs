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
/// <para>
/// It also ends a reply where the reply ends. After its reply the model sometimes writes a second
/// message in the same response: its own planning ("The assistant ended with two messages? ... we are
/// ChatGPT now"), the scene told a second time, a stray "(End of turn.)". In the simulated runs of 23
/// November 2026 that was 32 of 558 turns, every one of them in a response that followed a tool call.
/// The provider joins the second message on with a single line break, and the model's own prose never
/// puts one between sentences: its paragraphs are a blank line apart, and its single line breaks belong
/// to lists and verse. So a finished sentence, one line break and then more text is where the reply
/// ended; the rest is kept in <see cref="Dropped"/> for the turn trace and not shown.
/// ponytail: told from the text, because the chat-completions stream does not mark the boundary. Verse
/// whose line ends a sentence is cut there. The Responses API returns the messages as separate items;
/// read the boundary from those if the client moves to it.
/// </para>
/// </summary>
public sealed partial class StreamingScrubber
{
    private const int MinChunk = 32;
    private const int GuidLength = 36;
    // "12. " is the longest list marker looked for.
    private const int ListMarkerLength = 4;

    private string _pending = string.Empty;
    // The part of the current line already released: how a line ends decides whether a reply ended there.
    private string _line = string.Empty;
    private bool _replyEnded;
    private readonly System.Text.StringBuilder _dropped = new();

    /// <summary>True once any GUID has been removed.</summary>
    public bool Redacted { get; private set; }

    /// <summary>Everything the model wrote after a reply had ended. Never shown to the player.</summary>
    public string Dropped => _dropped.ToString();

    /// <summary>Call when the model's next response begins (a tool ran in between): it is a new reply,
    /// not more of one that was cut short.</summary>
    public void StartNewReply()
    {
        _replyEnded = false;
        _line = string.Empty;
    }

    /// <summary>Adds text; returns what is now safe to show (possibly empty).</summary>
    public string Append(string text)
    {
        if (_replyEnded)
        {
            _dropped.Append(text);
            return string.Empty;
        }

        _pending += text;
        var undecided = EndReplyWhereItEnds(noMoreText: false);
        if (_replyEnded) return Release(_pending.Length);

        var release = Math.Min(_pending.Length - HeldBack(_pending), undecided);
        return release < MinChunk ? string.Empty : Release(release);
    }

    /// <summary>Returns everything still held. Call when no more text can follow it directly.</summary>
    public string Flush()
    {
        if (!_replyEnded) EndReplyWhereItEnds(noMoreText: true);
        return Release(_pending.Length);
    }

    private string Release(int length)
    {
        var raw = _pending[..length];
        _pending = _pending[length..];
        var lineStart = raw.LastIndexOf('\n') + 1;
        _line = lineStart > 0 ? raw[lineStart..] : _line + raw;
        return Clean(raw);
    }

    /// <summary>Cuts the pending text at the end of the reply, if it is there. Returns the index of a
    /// line break that cannot be judged until more text arrives (the pending length when there is none):
    /// nothing from it on may be shown yet.</summary>
    private int EndReplyWhereItEnds(bool noMoreText)
    {
        var lineStart = 0;
        for (var i = _pending.IndexOf('\n'); i >= 0; i = _pending.IndexOf('\n', lineStart))
        {
            var line = (lineStart == 0 ? _line : string.Empty) + _pending[lineStart..i];
            lineStart = i + 1;
            if (!EndsAReply(line)) continue;

            var nextEnd = _pending.IndexOf('\n', lineStart);
            var next = nextEnd < 0 ? _pending[lineStart..] : _pending[lineStart..nextEnd];
            var nextIsWhole = nextEnd >= 0 || noMoreText;
            if (!nextIsWhole && next.Length < ListMarkerLength) return i;
            // A blank line makes a paragraph, a list marker a list, and nothing at all is just the end.
            if (next.Length == 0 || ListLine().IsMatch(next)) continue;

            _dropped.Append(_pending[i..]);
            _pending = _pending[..i];
            _replyEnded = true;
            break;
        }

        return _pending.Length;
    }

    /// <summary>A line that finishes a sentence and is not part of a list or of verse set with hard
    /// breaks (two trailing blanks).</summary>
    private static bool EndsAReply(string line)
    {
        if (line.EndsWith("  ", StringComparison.Ordinal) || ListLine().IsMatch(line)) return false;
        var text = line.TrimEnd();
        return text.Length > 0 && ".?!\u2026)\"\u201d\u2019*".Contains(text[^1]);
    }

    [GeneratedRegex(@"^\s*(?:[-*\u2022\u2013\u2014]|\d+[.)])\s")]
    private static partial Regex ListLine();

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
