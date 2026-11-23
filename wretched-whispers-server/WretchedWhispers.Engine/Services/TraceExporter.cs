using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Offline dump of every turn trace to a single JSON file for error analysis / eval failure-mode
/// discovery. Cross-user by design (all sessions). Invoked from Program.cs:
/// <c>dotnet run --project WretchedWhispers.Api -- export-traces [outDir]</c>.
/// </summary>
public static class TraceExporter
{
    public static async Task ExportAsync(IServiceProvider services, string outDir, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();

        var traces = await db.TurnTraces
            .AsNoTracking()
            .OrderBy(t => t.CampaignId)
            .ThenBy(t => t.ChatSessionId)
            .ThenBy(t => t.OrderIndex)
            .ToListAsync(ct);

        // The event log knows what the trace does not: the order in which narration and tool results
        // reached the player. Read whole — this is an offline dump — and matched to traces by turn.
        var events = (await db.TurnEvents
                .AsNoTracking()
                .Where(e => ShownEventTypes.Contains(e.EventType))
                .OrderBy(e => e.TurnId)
                .ThenBy(e => e.Sequence)
                .ToListAsync(ct))
            .ToLookup(e => e.TurnId);

        // Group into sessions; each turn re-embeds its stored JSON columns as real nested JSON.
        var sessions = new JsonArray();
        foreach (var group in traces
            .GroupBy(t => new { t.CampaignId, t.ChatSessionId })
            .OrderBy(g => g.Key.CampaignId))
        {
            var turns = new JsonArray();
            foreach (var t in group)
                turns.Add(new JsonObject
                {
                    ["orderIndex"] = t.OrderIndex,
                    ["timestamp"] = t.Timestamp.ToString("O"),
                    ["stage"] = t.Stage,
                    ["playerMessage"] = t.PlayerMessage,
                    ["gameState"] = Parse(t.GameStateJson),
                    ["toolCalls"] = Parse(t.ToolCallsJson),
                    ["toolResults"] = Parse(t.ToolResultsJson),
                    ["turnDelta"] = Parse(t.TurnDeltaJson),
                    ["suppressedNarrative"] = t.SuppressedNarrative,
                    ["narrative"] = t.Narrative,
                    ["turnId"] = t.TurnId?.ToString(),
                    ["attempt"] = t.Attempt,
                    ["outcome"] = t.Outcome,
                    ["error"] = t.Error,
                    ["modelId"] = t.ModelId,
                    ["promptHash"] = t.PromptHash,
                    ["toolsOffered"] = Parse(t.ToolsOffered),
                    ["inputTokens"] = t.InputTokens,
                    ["outputTokens"] = t.OutputTokens,
                    ["durationMs"] = t.DurationMs,
                    ["iterations"] = t.Iterations,
                    ["modelCalls"] = Parse(t.ModelCallsJson),
                    ["request"] = Parse(t.RequestJson),
                    ["events"] = t.TurnId is { } turnId && events.Contains(turnId) ? Shown(events[turnId]) : null
                });

            sessions.Add(new JsonObject
            {
                ["campaignId"] = group.Key.CampaignId.ToString(),
                ["chatSessionId"] = group.Key.ChatSessionId.ToString(),
                ["turnCount"] = turns.Count,
                ["turns"] = turns
            });
        }

        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "traces.json");
        var json = sessions.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);

        Console.WriteLine($"Exported {traces.Count} turns across {sessions.Count} sessions -> {path}");
    }

    // Outcome events (turn_delta, state_update, done) are left out: the trace already holds them as
    // turnDelta and the next turn's gameState.
    private static readonly string[] ShownEventTypes = ["narrative", "tool_result", "reset", "error"];

    /// <summary>
    /// A turn's event log as the player saw it, in order: narration (the pieces it streamed in, joined
    /// back together), each tool result where it arrived, "reset" where an earlier attempt's output
    /// was discarded, and the error if the turn failed. Each entry is the stored event plus its type.
    /// </summary>
    private static JsonArray Shown(IEnumerable<TurnEventEntity> events)
    {
        var shown = new JsonArray();
        foreach (var evt in events)
        {
            var payload = Parse(evt.Payload) as JsonObject ?? [];
            if (evt.EventType == "narrative" && shown.Count > 0
                && shown[^1] is JsonObject previous && (string?)previous["type"] == "narrative")
            {
                previous["text"] = (string?)previous["text"] + (string?)payload["text"];
                continue;
            }

            var entry = new JsonObject { ["type"] = evt.EventType };
            foreach (var (name, value) in payload)
                entry[name] = value?.DeepClone();
            shown.Add(entry);
        }
        return shown;
    }

    // Stored columns are already JSON. Parse so the output nests them instead of escaping strings.
    private static JsonNode? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
}
