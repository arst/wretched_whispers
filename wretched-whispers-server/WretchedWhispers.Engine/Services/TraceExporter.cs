using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Engine.Services;

/// <summary>
/// Offline dump of every turn trace to a single JSON file for error analysis / eval failure-mode
/// discovery. Cross-tenant by design (all sessions). Invoked from Program.cs:
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
                    ["narrative"] = t.Narrative
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

    // Stored columns are already JSON. Parse so the output nests them instead of escaping strings.
    private static JsonNode? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
}
