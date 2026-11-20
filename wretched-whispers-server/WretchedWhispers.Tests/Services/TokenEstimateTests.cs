using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class TokenEstimateTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("yes", 1)]
    [InlineData("four", 1)]
    [InlineData("fives", 2)]
    public void Text_IsAboutFourCharactersPerToken_RoundedUp(string? text, int expected)
    {
        Assert.Equal(expected, TokenEstimate.For(text));
    }

    [Fact]
    public void Message_AddsASmallFramingOverhead()
    {
        Assert.Equal(100 + 4, TokenEstimate.For(new ChatMessage(ChatRole.User, new string('x', 400))));
    }

    private static HistoryBudget BudgetFrom(params (string Key, string Value)[] settings) =>
        HistoryBudget.From(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build());

    [Fact]
    public void Budget_Defaults()
    {
        Assert.Equal(new HistoryBudget(12_000, 8_000), BudgetFrom());
    }

    [Fact]
    public void Budget_ReadsConfiguration()
    {
        var budget = BudgetFrom(
            ("GameSession:HistoryTriggerTokens", "30000"), ("GameSession:HistoryKeepTokens", "20000"));

        Assert.Equal(new HistoryBudget(30_000, 20_000), budget);
    }

    [Fact]
    public void Budget_KeepIsHeldBelowTheTrigger_SoSummarizingStaysOccasional()
    {
        var budget = BudgetFrom(
            ("GameSession:HistoryTriggerTokens", "10000"), ("GameSession:HistoryKeepTokens", "10000"));

        Assert.Equal(10_000, budget.TriggerTokens);
        Assert.Equal(7_500, budget.KeepTokens);
    }
}
