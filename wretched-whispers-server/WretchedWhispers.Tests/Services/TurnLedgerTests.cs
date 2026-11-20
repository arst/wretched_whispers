using System.Text.Json;
using Microsoft.Extensions.AI;
using WretchedWhispers.Engine.Models;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public class TurnLedgerTests
{
    private static ChatMessage Player(string text, params ToolResult[] results)
    {
        var message = new ChatMessage(ChatRole.User, text);
        TurnLedger.Attach(message, results);
        return message;
    }

    private static ToolResult Ok(string name, string? args) => new(name, "{}", args);

    [Fact]
    public void Record_NamesEachToolWithItsArguments_InCallOrder()
    {
        var message = Player("I strike the priest!",
            Ok("ResolveCombatRound", """{"action":"Attack","targetAdversaryName":"Priest"}"""),
            Ok("RecordJournalEntry", """{"category":"Event","text":"The priest fell"}"""));

        Assert.Equal(
            "[Engine record, not shown to the player — tools run for the message above: "
            + """ResolveCombatRound(action: "Attack", targetAdversaryName: "Priest"); """
            + """RecordJournalEntry(category: "Event", text: "The priest fell")]""",
            TurnLedger.RecordText(message));
    }

    [Fact]
    public void FailedCalls_AreLeftOut_AndATurnWithNoneThatWorked_HasNoRecord()
    {
        var mixed = Player("I roll",
            new ToolResult("Roll", "Error: bad dice", """{"diceExpression":"banana"}""", Failed: true),
            Ok("Roll", """{"diceExpression":"d6"}"""));
        var allFailed = Player("I roll",
            new ToolResult("Roll", "Error: bad dice", """{"diceExpression":"banana"}""", Failed: true));

        Assert.DoesNotContain("banana", TurnLedger.RecordText(mixed));
        Assert.Contains("""Roll(diceExpression: "d6")""", TurnLedger.RecordText(mixed));
        Assert.Null(TurnLedger.RecordText(allFailed));
        Assert.Null(allFailed.AdditionalProperties);
    }

    [Fact]
    public void ToolWithoutArguments_AndAPlainMessage()
    {
        Assert.Contains("StartEncounter()", TurnLedger.RecordText(Player("fight!", Ok("StartEncounter", null))));
        Assert.Null(TurnLedger.RecordText(new ChatMessage(ChatRole.User, "hello")));
    }

    [Fact]
    public void LongArguments_AreCut_SoOneCallCannotBloatTheRecord()
    {
        var args = JsonSerializer.Serialize(new { category = "Event", text = new string('x', 2_000) });

        var record = TurnLedger.RecordText(Player("note this", Ok("RecordJournalEntry", args)))!;

        Assert.True(record.Length < 300, $"record was {record.Length} characters");
        Assert.Contains("…", record);
    }

    [Fact]
    public void Record_SurvivesTheStore_WhereTheValueComesBackAsAJsonElement()
    {
        var saved = Player("I rest", Ok("Rest", """{"hours":8}"""));
        // What the chat repository does: metadata is serialized on save and deserialized on load.
        var stored = JsonSerializer.Serialize(saved.AdditionalProperties);
        var loaded = new ChatMessage(ChatRole.User, "I rest")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary(
                JsonSerializer.Deserialize<Dictionary<string, object?>>(stored)!),
        };

        Assert.IsType<JsonElement>(loaded.AdditionalProperties[TurnLedger.Key]);
        Assert.Contains("Rest(hours: 8)", TurnLedger.RecordText(loaded));
    }

    [Fact]
    public void UnreadableRecord_IsIgnored()
    {
        var message = new ChatMessage(ChatRole.User, "hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [TurnLedger.Key] = "not json" },
        };

        Assert.Null(TurnLedger.RecordText(message));
    }

    [Fact]
    public void Expand_InsertsASystemRecord_RightAfterEachPlayerMessageThatHasOne()
    {
        var history = new List<ChatMessage>
        {
            Player("I rest", Ok("Rest", """{"hours":8}""")),
            new(ChatRole.Assistant, "You sleep."),
            new(ChatRole.User, "What do I see?"),
            new(ChatRole.Assistant, "Fog."),
            Player("I buy rope", Ok("BuyItem", """{"itemDescription":"rope","silverCost":4}""")),
            new(ChatRole.Assistant, "The crone takes your silver."),
        };

        var expanded = TurnLedger.Expand(history);

        Assert.Equal(8, expanded.Count);
        Assert.Equal(
            new[] { "user", "system", "assistant", "user", "assistant", "user", "system", "assistant" },
            expanded.Select(m => m.Role.Value));
        Assert.Contains("Rest(hours: 8)", expanded[1].Text);
        Assert.Contains("BuyItem(", expanded[6].Text);
        // The stored messages themselves are untouched.
        Assert.Equal("I rest", expanded[0].Text);
        Assert.Equal("You sleep.", expanded[2].Text);
    }

    [Fact]
    public void Expand_WithNoRecords_ReturnsTheHistoryAsIs()
    {
        IReadOnlyList<ChatMessage> history =
            [new(ChatRole.User, "hello"), new(ChatRole.Assistant, "The world rots.")];

        Assert.Same(history, TurnLedger.Expand(history));
    }
}
