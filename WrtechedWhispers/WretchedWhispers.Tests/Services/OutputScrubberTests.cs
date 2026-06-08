using WretchedWhispers.Api.Services;
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
        Assert.DoesNotContain("-", result); // both GUIDs (and their hyphens) gone
        Assert.Contains("crawl from the dark.", result);
    }
}
