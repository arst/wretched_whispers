using System.Diagnostics;
using System.Net.ServerSentEvents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Api.Endpoints;
using WretchedWhispers.Infrastructure.Persistence;
using Xunit;

namespace WretchedWhispers.Tests.Sessions;

/// <summary>
/// The events endpoint's tail over the turn event store. File-backed SQLite, so the tail and the
/// "worker" appending events use separate connections, as they do in production.
/// </summary>
public sealed class TurnEventTailTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ww-eventtail-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;
    private readonly TurnEventStore _store;
    private readonly Guid _turnId = Guid.NewGuid();

    public TurnEventTailTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite($"DataSource={_dbPath}"));
        _provider = services.BuildServiceProvider();
        using (var scope = _provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>().Database.EnsureCreated();
        _store = new TurnEventStore(_provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_dbPath + suffix);
    }

    private Task AppendAsync(string type, object payload) =>
        _store.AppendAsync(_turnId, type, payload, TestContext.Current.CancellationToken);

    private async Task<List<SseItem<string>>> TailAsync(TurnEventTailOptions options, long from = 0)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var items = new List<SseItem<string>>();
        await foreach (var item in TurnEndpoints.TailAsync(
                           _store, _turnId, from, alreadyFinished: false, terminalError: null, options, timeout.Token))
            items.Add(item);
        Assert.False(timeout.IsCancellationRequested, "the tail did not reach a terminal event in time");
        return items;
    }

    [Fact]
    public void DefaultPollInterval_IsAQuarterSecond()
    {
        var options = TurnEventTailOptions.From(new ConfigurationBuilder().Build());

        Assert.Equal(TimeSpan.FromMilliseconds(250), options.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), options.KeepaliveInterval);
    }

    [Fact]
    public async Task StoredEvents_AreReplayedInOrder_FromTheGivenSequence_UpToTheTerminalEvent()
    {
        await AppendAsync("narrative", new { text = "one" });
        await AppendAsync("narrative", new { text = "two" });
        await AppendAsync("done", new { });

        var items = await TailAsync(new(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(30)), from: 1);

        Assert.Equal(new[] { "narrative", "done" }, items.Select(i => i.EventType));
        Assert.Equal(new[] { "2", "3" }, items.Select(i => i.EventId));
        Assert.Contains("two", items[0].Data);
    }

    [Fact]
    public async Task EventsSavedWhileTailing_AreRelayedAtThePollPace_NotTheKeepaliveBeat()
    {
        // Poll fast, keepalive (effectively) never: everything the tail yields is driven by the poll.
        var tail = TailAsync(new(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(30)));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        await AppendAsync("narrative", new { text = "The door groans open." });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await AppendAsync("done", new { });

        var items = await tail;
        Assert.Equal(new[] { "narrative", "done" }, items.Select(i => i.EventType));
    }

    [Fact]
    public async Task Keepalives_KeepTheirOwnBeat_HoweverOftenTheStoreIsChecked()
    {
        var keepalive = TimeSpan.FromMilliseconds(200);
        var watch = Stopwatch.StartNew();
        var tail = TailAsync(new(TimeSpan.FromMilliseconds(10), keepalive));

        await Task.Delay(700, TestContext.Current.CancellationToken);
        await AppendAsync("done", new { });

        var items = await tail;
        var quiet = watch.Elapsed;
        var keepalives = items.Count(i => i.EventType == "keepalive");
        // ~70 store checks happened in the quiet stretch; keepalives follow the clock, not the checks.
        Assert.InRange(keepalives, 1, (int)(quiet / keepalive) + 1);
        Assert.Equal("done", items[^1].EventType);
    }
}
