using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class OutputScrubberTests
{
    [Fact]
    public void NormalProse_IsUntouched()
    {
        const string prose = "Grim claws free of the muck, doomed already. The bells toll once.";

        var result = OutputScrubber.RedactGuids(prose, out var redacted);

        Assert.False(redacted);
        Assert.Equal(prose, result);
    }

    [Fact]
    public void LeakedGuid_IsRemoved()
    {
        const string leaked = "The sludge rat 7504b8e9-3c59-47b6-b38b-96c0bb5f30bd lunges at you.";

        var result = OutputScrubber.RedactGuids(leaked, out var redacted);

        Assert.True(redacted);
        Assert.DoesNotContain("7504b8e9", result);
        Assert.DoesNotContain("-3c59-", result);
        Assert.Contains("The sludge rat", result);
        Assert.Contains("lunges at you.", result);
    }

    [Fact]
    public void ParentheticalIdAnnotation_IsTidiedUp()
    {
        const string leaked = "You strike the adversary (id: 0a8aa0e4-9555-40df-81c2-f14d33713d0d) hard.";

        var result = OutputScrubber.RedactGuids(leaked, out var redacted);

        Assert.True(redacted);
        Assert.DoesNotContain("0a8aa0e4", result);
        Assert.DoesNotContain("(id: )", result);
        Assert.Contains("You strike the adversary", result);
    }

    [Fact]
    public void MultipleGuids_AllRemoved()
    {
        var text = $"A {Guid.NewGuid()} and a {Guid.NewGuid()} crawl from the dark.";

        var result = OutputScrubber.RedactGuids(text, out var redacted);

        Assert.True(redacted);
        Assert.DoesNotMatch(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", result);
        Assert.Contains("crawl from the dark.", result);
    }

    // ---- StreamingScrubber: the same result, however the text is cut into pieces ----

    private static string Stream(IEnumerable<string> pieces)
    {
        var scrubber = new StreamingScrubber();
        var output = new System.Text.StringBuilder();
        foreach (var piece in pieces)
            output.Append(scrubber.Append(piece));
        return output.Append(scrubber.Flush()).ToString();
    }

    public static TheoryData<string> StreamedTexts => new()
    {
        "The sludge rat 7504b8e9-3c59-47b6-b38b-96c0bb5f30bd lunges at you from the dark of the cellar.",
        "You face the priest (id: 7504b8e9-3c59-47b6-b38b-96c0bb5f30bd) across the nave, and he laughs.",
        "Two of them: 7504b8e9-3c59-47b6-b38b-96c0bb5f30bd and (11111111-2222-3333-4444-555555555555) wait.",
        "No ids here at all — only a bad, faded cafe facade and a dead-end road (so they say) to Galgenbeck.",
        "A decade of dread: 1234-5678 is no id, nor is deadbeef-dead-beef, nor a lone ( in the text.",
    };

    [Theory]
    [MemberData(nameof(StreamedTexts))]
    public void Streaming_OneCharacterAtATime_MatchesScrubbingTheWholeText(string text)
    {
        var expected = OutputScrubber.RedactGuids(text, out _);

        Assert.Equal(expected, Stream(text.Select(c => c.ToString())));
    }

    [Theory]
    [MemberData(nameof(StreamedTexts))]
    public void Streaming_SplitAnywhere_MatchesScrubbingTheWholeText(string text)
    {
        var expected = OutputScrubber.RedactGuids(text, out _);

        for (var cut = 0; cut <= text.Length; cut++)
            Assert.Equal(expected, Stream([text[..cut], text[cut..]]));
    }

    [Fact]
    public void Streaming_NeverReleasesAnyPartOfAGuid_BeforeItIsComplete()
    {
        const string text = "The sludge rat 7504b8e9-3c59-47b6-b38b-96c0bb5f30bd lunges at you from the dark.";
        var scrubber = new StreamingScrubber();

        foreach (var c in text)
            Assert.DoesNotContain("7504", scrubber.Append(c.ToString()));

        Assert.True(scrubber.Redacted);
    }

    [Fact]
    public void Streaming_ReleasesPlainProseWithoutWaitingForTheEnd()
    {
        var scrubber = new StreamingScrubber();

        var released = scrubber.Append("The chapel door hangs open, and the wind carries the smell of old smoke. ");

        Assert.StartsWith("The chapel door hangs open", released);
    }

    // ---- StreamingScrubber: a reply ends once ----
    //
    // After its reply the model sometimes writes a second message: its own planning, the scene told
    // again, a stray "(End of turn.)". The provider joins it on with a single line break, which the
    // model's own prose never uses between sentences (paragraphs are a blank line apart). The samples
    // are from the simulated runs of 23 November 2026, shortened.

    private static (string Shown, string Dropped) StreamReply(string text, int pieceLength)
    {
        var scrubber = new StreamingScrubber();
        var shown = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i += pieceLength)
            shown.Append(scrubber.Append(text.Substring(i, Math.Min(pieceLength, text.Length - i))));
        shown.Append(scrubber.Flush());
        return (shown.ToString(), scrubber.Dropped);
    }

    public static TheoryData<string, string> RepliesWithASecondMessage => new()
    {
        {
            "The scavenger grins and raises the pole.\n\nIt is the start of combat; make your first move. What do you do?",
            "\nThe previous message already ended with \"What do you do?\" You must respond to the player's next action."
        },
        {
            "You may trade, use the perfume to bribe, or wait longer. Choose and I will act.",
            "\n(End of turn.)"
        },
        {
            "The old woman points east.\n\nThe thin light presses against the buildings; the city listens.",
            "\nI repeated last message accidentally; no game state changed. Now await player's input.\n(You can ask the old woman about a person.)"
        },
        {
            "You can:\n- Call down now and start a trade,\n- Wait an hour.\n\nYour move. What do you do?",
            "\nThe bells have cracked and stopped. The sky spat ash overnight.\n\nYou wake beneath a torn fresco. What do you do?"
        },
        {
            "\u201cThe gate is held by men who took the bells,\u201d she says. \u201cGo the way the rats go.\u201d",
            "\nThe assistant ended with two messages? But last real message finished. We are ChatGPT now."
        },
    };

    [Theory]
    [MemberData(nameof(RepliesWithASecondMessage))]
    public void Streaming_DropsWhatFollowsTheEndOfTheReply(string reply, string secondMessage)
    {
        foreach (var pieceLength in new[] { 1, 3, 7, 40, 10_000 })
        {
            var (shown, dropped) = StreamReply(reply + secondMessage, pieceLength);

            Assert.Equal(reply, shown);
            Assert.Equal(secondMessage, dropped);
        }
    }

    public static TheoryData<string> RepliesThatAreOneMessage => new()
    {
        "The door hangs open.\n\nInside, something breathes. What do you do?",
        "Your state, as it sits like a coin on a palm:\n- HP 5/5\n- Strength 0, Agility -1\n- Weapon: Knife (d4)\n\nWhat do you do?",
        "Options:\n1. Climb down now.\n2. Wait for dark.\n3. Call out.\nChoose.",
        "What do you do now? You can:\n* Search the bones.\n* Attempt a ritual.",
        "Spring hobbles in on threadbare green,\niron snow unraveling into river-bile,\nand nothing blooms.",
        "A trout rips through salt and bone, pale as a liar's promise.  \nGulls stitch the sky with laughter.  \nThe tide keeps count.",
        "It is quiet. What do you do?\n",
        "It is quiet. What do you do?\n\n",
    };

    [Theory]
    [MemberData(nameof(RepliesThatAreOneMessage))]
    public void Streaming_KeepsARepliesOwnLineBreaks(string reply)
    {
        foreach (var pieceLength in new[] { 1, 3, 7, 40, 10_000 })
        {
            var (shown, dropped) = StreamReply(reply, pieceLength);

            Assert.Equal(reply, shown);
            Assert.Equal("", dropped);
        }
    }

    // The next model response of the turn (after a tool ran) is a new reply, not more of the dropped one.
    [Fact]
    public void Streaming_ANewReplyIsShownAfterOneWasCutShort()
    {
        var scrubber = new StreamingScrubber();
        var first = scrubber.Append("You draw the knife. What do you do?\n(End of turn.)") + scrubber.Flush();
        scrubber.StartNewReply();

        var second = scrubber.Append("The blade bites and the ghoul staggers back.") + scrubber.Flush();

        Assert.Equal("You draw the knife. What do you do?", first);
        Assert.Equal("The blade bites and the ghoul staggers back.", second);
        Assert.Equal("\n(End of turn.)", scrubber.Dropped);
    }
}
