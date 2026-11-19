using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Entities;

namespace WretchedWhispers.Engine.Services;

/// <summary>Timing knobs for <see cref="TurnWorker"/>. The defaults are the production values.</summary>
public sealed record TurnWorkerOptions
{
    /// <summary>Overall time budget for one attempt at a turn, across all its model requests.</summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often a running turn checks whether the player asked to cancel it.</summary>
    public TimeSpan CancelPollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Pause before a transiently failed turn goes back on the queue.</summary>
    public TimeSpan RetryBackoff { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxAttempts { get; init; } = 3;
}

public sealed class TurnWorker(
    IServiceScopeFactory scopes, TurnEventStore events, ILogger<TurnWorker> logger,
    TurnWorkerOptions? options = null) : BackgroundService
{
    public const string DeadlineMessage = "The narrator took too long. Please try again.";

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RenewEvery = TimeSpan.FromMinutes(1);

    private readonly TurnWorkerOptions _options = options ?? new TurnWorkerOptions();
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
                var turn = await queue.ClaimAsync(_owner, Lease, _options.MaxAttempts, stoppingToken);
                if (turn is null) { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); continue; }
                if (turn.Status == TurnStatus.Failed)
                    continue;

                // Two cancellation scopes. `execution` ends when WE must stop working on the turn
                // altogether: shutdown, or another instance reclaimed it (the new owner decides the
                // outcome; its duplicate-answer check reconciles whatever we managed to commit).
                // `run` additionally ends when THIS attempt must stop but the turn is still ours to
                // finalize: the deadline passed, or the player cancelled.
                //
                // A turn can outlive any fixed lease (a single model call runs minutes), so the lease
                // is renewed while we work; expiry then means the owner actually died.
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var run = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
                run.CancelAfter(_options.Deadline);
                var cancel = new CancelSignal();
                var watch = WatchAsync(turn.Id, execution, run, cancel);
                try
                {
                    var chatHistory = scope.ServiceProvider.GetRequiredService<IChatHistoryRepository>();
                    if (await chatHistory.HasMessagesForTurn(turn.Id, execution.Token))
                    {
                        await queue.FinalizeAsync(turn.Id, _owner, null, execution.Token);
                        continue;
                    }

                    scope.ServiceProvider.GetRequiredService<IUserContext>().SetUserId(turn.UserId);
                    string? error = null;
                    var retry = false;
                    try
                    {
                        var ended = false;
                        var coordinator = scope.ServiceProvider.GetRequiredService<TurnCoordinator>();
                        await foreach (var item in coordinator.ExecuteTurnAsync(
                            turn.CampaignId, turn.PlayerMessage, turn.Id, run.Token, turn.AttemptCount))
                        {
                            if (item.EventType is not ("done" or "error"))
                                await events.AppendAsync(turn.Id, item.EventType, item, execution.Token);
                            if (item is Models.TurnError turnError)
                            {
                                error = turnError.Message;
                                retry = turnError.Retryable;
                            }
                            ended |= item is Models.TurnError or Models.TurnDone;
                        }
                        // A cancelled run can end its stream quietly instead of throwing.
                        if (!ended) run.Token.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException) when (run.IsCancellationRequested && !execution.IsCancellationRequested)
                    {
                        // The deadline or the player stopped this attempt; the turn is still ours. If the
                        // commit won the race the turn happened — it completes, and the client reloads
                        // the chronicle, exactly as on the crash-replay path. Otherwise nothing was
                        // committed or shown, and the turn fails with the reason.
                        if (!await chatHistory.HasMessagesForTurn(turn.Id, execution.Token))
                        {
                            error = cancel.Requested ? TurnQueue.CancelledMessage : DeadlineMessage;
                            logger.LogWarning("Turn {TurnId} stopped before committing: {Reason}", turn.Id, error);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // A failure OUTSIDE the coordinator's own net — resolving it (a misconfigured
                        // LLM client throws at DI time) or persisting its events. The coordinator turns
                        // its failures into a terminal error event; these must end the same way, or the
                        // client tails keepalives while the lease cycle burns the attempts for minutes.
                        logger.LogError(ex, "Turn failed outside the coordinator — Turn={TurnId}", turn.Id);
                        error = "An error occurred while processing your action";
                    }

                    if (retry && turn.AttemptCount < _options.MaxAttempts && !cancel.Requested)
                    {
                        // Transient, and nothing happened: give the provider a moment, then let any
                        // worker take another attempt. The last allowed attempt fails with its error.
                        logger.LogWarning(
                            "Turn {TurnId} attempt {Attempt} failed transiently; requeueing", turn.Id, turn.AttemptCount);
                        await Task.Delay(_options.RetryBackoff, execution.Token);
                        if (!await queue.RequeueAsync(turn.Id, _owner, execution.Token))
                            logger.LogWarning("Lost the lease before requeueing turn {TurnId}", turn.Id);
                    }
                    else if (!await queue.FinalizeAsync(turn.Id, _owner, error, execution.Token))
                        logger.LogWarning("Lost the lease before finalizing turn {TurnId}", turn.Id);
                    failures = 0;
                }
                finally
                {
                    await execution.CancelAsync();
                    await watch;
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

    private sealed class CancelSignal
    {
        private volatile bool _requested;
        public bool Requested => _requested;
        public void Set() => _requested = true;
    }

    /// <summary>
    /// Watches a running turn: stops the attempt when the player asks to cancel, and renews the lease.
    /// Never throws — both are best-effort until the lease is definitively lost.
    /// </summary>
    private async Task WatchAsync(
        Guid turnId, CancellationTokenSource execution, CancellationTokenSource run, CancelSignal cancel)
    {
        using var timer = new PeriodicTimer(_options.CancelPollInterval);
        var sinceRenewal = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (await timer.WaitForNextTickAsync(execution.Token))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var queue = scope.ServiceProvider.GetRequiredService<TurnQueue>();

                    if (!cancel.Requested && await queue.IsCancelRequestedAsync(turnId, execution.Token))
                    {
                        logger.LogInformation("Cancel requested for turn {TurnId}; stopping its run", turnId);
                        cancel.Set();
                        await run.CancelAsync();
                    }

                    if (sinceRenewal.Elapsed < RenewEvery) continue;
                    sinceRenewal.Restart();
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
                    logger.LogWarning(ex, "Turn watch failed for turn {TurnId}; retrying", turnId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
