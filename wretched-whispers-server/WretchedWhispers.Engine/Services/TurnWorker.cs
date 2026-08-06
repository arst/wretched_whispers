using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Engine.Services;

public sealed class TurnWorker(IServiceScopeFactory scopes, TurnEventStore events, ILogger<TurnWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<TurnQueue>();
                var turn = await queue.ClaimAsync(_owner, Lease, 3, stoppingToken);
                if (turn is null) { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); continue; }
                if (turn.Status == TurnStatus.Failed)
                {
                    await events.AppendTerminalAsync(turn.Id, "error", new { message = turn.TerminalError }, stoppingToken);
                    continue;
                }

                // A turn can outlive any fixed lease (a single model call runs minutes), so the lease
                // is renewed while we work; expiry then means the owner actually died. Losing the
                // renewal race means another instance reclaimed the turn — cancel our execution, the
                // new owner decides the outcome (its duplicate-answer check below reconciles whatever
                // we managed to commit).
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var renewal = RenewLeaseAsync(turn.Id, execution);
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
                    if (await db.ChatMessages.AnyAsync(x => x.TurnId == turn.Id && x.Role == ChatRole.Assistant.Value, execution.Token))
                    {
                        await events.AppendTerminalAsync(turn.Id, "done", new { }, execution.Token);
                        await queue.CompleteAsync(turn.Id, _owner, null, execution.Token);
                        continue;
                    }

                    scope.ServiceProvider.GetRequiredService<IUserContext>().SetUserId(turn.UserId);
                    var coordinator = scope.ServiceProvider.GetRequiredService<TurnCoordinator>();
                    string? error = null;
                    await foreach (var item in coordinator.ExecuteTurnAsync(turn.CampaignId, turn.PlayerMessage, execution.Token, turn.Id))
                    {
                        await events.AppendAsync(turn.Id, item.EventType, item, execution.Token);
                        if (item is Models.TurnError turnError) error = turnError.Message;
                    }
                    await queue.CompleteAsync(turn.Id, _owner, error, execution.Token);
                    failures = 0;
                }
                finally
                {
                    await execution.CancelAsync();
                    await renewal;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Turn execution cancelled after losing its lease to another worker");
            }
            catch (Exception ex)
            {
                // ponytail: back off so a broken database doesn't spam a stack trace every 250ms
                var delay = TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, failures++), 30_000));
                logger.LogError(ex, "Durable turn worker iteration failed; retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    /// <summary>Never throws: renewal is best-effort until it definitively loses the lease.</summary>
    private async Task RenewLeaseAsync(Guid turnId, CancellationTokenSource execution)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(execution.Token))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var queue = scope.ServiceProvider.GetRequiredService<TurnQueue>();
                    if (!await queue.RenewAsync(turnId, _owner, Lease, execution.Token))
                    {
                        logger.LogWarning("Lost the lease on turn {TurnId}; cancelling its execution", turnId);
                        await execution.CancelAsync();
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Transient database trouble: keep executing and retry at the next tick — if the
                    // database is really gone the turn itself fails on its own.
                    logger.LogWarning(ex, "Lease renewal failed for turn {TurnId}; retrying", turnId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
