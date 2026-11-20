using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WretchedWhispers.Engine.Models;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// The record of which tools a turn ran, kept with the turn's player message and shown to the model
/// — and only to the model — on later turns.
///
/// Chat history stores prose only. Without this the model looks back on a conversation in which
/// outcomes simply appear in the Game Master's narration with no tool call anywhere in sight, which
/// is exactly the pattern it must not continue. The ledger puts the calls back: after each earlier
/// player message the model sees one system line naming the tools that ran for it, with the arguments
/// it chose (target names, item descriptions, difficulty ratings).
///
/// Deliberately NOT included: the tools' results. They are full of mechanical state (hit points,
/// silver, inventory) that goes stale and would then contradict the live Game State, the same reason
/// the summary no longer carries it; what the results meant is in the narration that followed.
/// </summary>
public static class TurnLedger
{
    /// <summary>Key under which the ledger is stored in a player message's AdditionalProperties.</summary>
    public const string Key = "ww.toolCalls";

    /// <summary>Per-call cap on rendered arguments, so one long journal text cannot bloat the record.</summary>
    private const int MaxArgumentChars = 160;

    private sealed record Call(string Name, JsonElement? Arguments);

    /// <summary>
    /// Stores the turn's successful tool calls on its player message. Calls that failed changed
    /// nothing and are left out, so the model is not shown its own rejected attempts as precedent.
    /// </summary>
    public static void Attach(ChatMessage playerMessage, IEnumerable<ToolResult> toolResults)
    {
        var calls = toolResults
            .Where(t => !t.Failed)
            .Select(t => new Call(t.Function, ParseArguments(t.Arguments)))
            .ToList();
        if (calls.Count == 0) return;

        playerMessage.AdditionalProperties ??= [];
        playerMessage.AdditionalProperties[Key] = JsonSerializer.Serialize(calls, JsonSerializerOptions.Web);
    }

    /// <summary>The text of the model-facing record for this message's turn, or null when it has none.</summary>
    public static string? RecordText(ChatMessage message)
    {
        if (message.AdditionalProperties?.TryGetValue(Key, out var stored) is not true) return null;

        // A value we just attached is a string; one loaded from the store arrives as a JsonElement.
        var json = stored switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        };
        if (string.IsNullOrEmpty(json)) return null;

        List<Call>? calls;
        try
        {
            calls = JsonSerializer.Deserialize<List<Call>>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null; // a record we cannot read is no reason to fail a turn
        }
        if (calls is null || calls.Count == 0) return null;

        return "[Engine record, not shown to the player — tools run for the message above: "
            + string.Join("; ", calls.Select(Render)) + "]";
    }

    /// <summary>
    /// The history as the model should see it: after every player message that has a ledger, a system
    /// message carrying its record. System role on purpose — the model does not imitate system
    /// messages in its own narration the way it would a bracketed line inside its earlier replies.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Expand(IReadOnlyList<ChatMessage> history)
    {
        List<ChatMessage>? expanded = null;
        for (var i = 0; i < history.Count; i++)
        {
            expanded?.Add(history[i]);
            if (RecordText(history[i]) is not { } record) continue;

            expanded ??= [.. history.Take(i + 1)];
            expanded.Add(new ChatMessage(ChatRole.System, record));
        }

        return expanded ?? history;
    }

    private static string Render(Call call)
    {
        if (call.Arguments is not { ValueKind: JsonValueKind.Object } arguments)
            return $"{call.Name}()";

        var rendered = new StringBuilder();
        foreach (var argument in arguments.EnumerateObject())
        {
            if (rendered.Length > 0) rendered.Append(", ");
            rendered.Append(argument.Name).Append(": ").Append(argument.Value.GetRawText());
        }

        var text = rendered.Length > MaxArgumentChars
            ? string.Concat(rendered.ToString().AsSpan(0, MaxArgumentChars), "…")
            : rendered.ToString();
        return $"{call.Name}({text})";
    }

    private static JsonElement? ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
