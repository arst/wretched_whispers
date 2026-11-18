using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Engine.Services;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;
using Xunit;

namespace WretchedWhispers.Tests.Services;

public sealed class TraceExporterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), $"ww-traces-{Guid.NewGuid():N}");

    public TraceExporterTests() => _connection.Open();

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true);
    }

    [Fact]
    public async Task Export_IncludesOutcomeModelPromptAndUsageFields()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite(_connection));
        await using var provider = services.BuildServiceProvider();
        var turnId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            db.TurnTraces.Add(new TurnTraceEntity
            {
                Id = Guid.NewGuid(),
                CampaignId = Guid.NewGuid(),
                ChatSessionId = Guid.NewGuid(),
                Stage = "Combat",
                Timestamp = DateTime.UtcNow,
                PlayerMessage = "I attack",
                TurnId = turnId,
                Attempt = 2,
                Outcome = TurnTraceOutcome.Failed,
                Error = "InvalidOperationException: boom",
                ModelId = "gpt-test",
                PromptHash = "abc123",
                ToolsOffered = "[\"ResolveCombatRound\"]",
                InputTokens = 1200,
                OutputTokens = 85,
                DurationMs = 4321,
                Iterations = 2,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await TraceExporter.ExportAsync(provider, _outDir, TestContext.Current.CancellationToken);

        var turn = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(_outDir, "traces.json"), TestContext.Current.CancellationToken))![0]!["turns"]![0]!;
        Assert.Equal(turnId.ToString(), turn["turnId"]!.GetValue<string>());
        Assert.Equal(2, turn["attempt"]!.GetValue<int>());
        Assert.Equal("failed", turn["outcome"]!.GetValue<string>());
        Assert.Equal("InvalidOperationException: boom", turn["error"]!.GetValue<string>());
        Assert.Equal("gpt-test", turn["modelId"]!.GetValue<string>());
        Assert.Equal("abc123", turn["promptHash"]!.GetValue<string>());
        Assert.Equal("ResolveCombatRound", turn["toolsOffered"]![0]!.GetValue<string>());
        Assert.Equal(1200, turn["inputTokens"]!.GetValue<long>());
        Assert.Equal(85, turn["outputTokens"]!.GetValue<long>());
        Assert.Equal(4321, turn["durationMs"]!.GetValue<long>());
        Assert.Equal(2, turn["iterations"]!.GetValue<int>());
    }
}
