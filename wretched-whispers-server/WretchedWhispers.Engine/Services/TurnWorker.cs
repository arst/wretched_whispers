using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WretchedWhispers.Infrastructure;
using WretchedWhispers.Infrastructure.Persistence;

namespace WretchedWhispers.Engine.Services;

public sealed class TurnWorker(IServiceScopeFactory scopes, TurnEventStore events, ILogger<TurnWorker> logger) : BackgroundService
{
    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<TurnQueue>();
                var turn = await queue.ClaimAsync(_owner, TimeSpan.FromMinutes(5), 3, stoppingToken);
                if (turn is null) { await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken); continue; }
                if (turn.Status == "Failed")
                {
                    await events.AppendTerminalAsync(turn.Id, "error", new { message = turn.TerminalError }, stoppingToken);
                    continue;
                }

                var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
                if (await db.ChatMessages.AnyAsync(x => x.TurnId == turn.Id && x.Role == "assistant", stoppingToken))
                {
                    await events.AppendTerminalAsync(turn.Id, "done", new { }, stoppingToken);
                    await queue.CompleteAsync(turn.Id, null, stoppingToken);
                    continue;
                }

                scope.ServiceProvider.GetRequiredService<IUserContext>().SetUserId(turn.UserId);
                var coordinator = scope.ServiceProvider.GetRequiredService<TurnCoordinator>();
                string? error = null;
                await foreach (var item in coordinator.ExecuteTurnAsync(turn.CampaignId, turn.PlayerMessage, stoppingToken, turn.Id))
                {
                    await events.AppendAsync(turn.Id, item.EventType, item, stoppingToken);
                    if (item is Models.TurnError turnError) error = turnError.Message;
                }
                await queue.CompleteAsync(turn.Id, error, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Durable turn worker iteration failed"); await Task.Delay(250, stoppingToken); }
        }
    }
}
