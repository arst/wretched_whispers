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
}
