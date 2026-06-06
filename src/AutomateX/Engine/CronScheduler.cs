using System.Text.Json;
using AutomateX.Database;
using AutomateX.Modules.Triggers;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Engine;

public sealed class CronScheduler(
    IServiceProvider serviceProvider,
    ILogger<CronScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FireDueTriggersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cron scheduler tick failed");
            }
        }
    }

    private async Task FireDueTriggersAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // The Npgsql retrying strategy forbids ad-hoc transactions, so the whole
        // claim-fire-reschedule unit runs as one retriable block.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();

            // SKIP LOCKED makes claiming safe across multiple nodes; the row locks hold until commit.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var due = await dbContext.Triggers
                .FromSqlRaw("""
                    SELECT * FROM "Triggers"
                    WHERE "Type" = 'cron' AND "Enabled" AND "NextRunAt" <= now()
                    ORDER BY "NextRunAt"
                    LIMIT 20
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var trigger in due)
            {
                var executionId = Guid.CreateVersion7();
                await bus.PublishAsync(new RunWorkflow(executionId, trigger.WorkflowId, $"cron:{trigger.Id}"));
                trigger.MarkFired(ComputeNextRunAt(trigger));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private DateTimeOffset? ComputeNextRunAt(Trigger trigger)
    {
        try
        {
            var config = JsonSerializer.Deserialize<CronTriggerConfig>(trigger.ConfigJson, JsonSerializerOptions.Web);
            if (string.IsNullOrWhiteSpace(config?.Cron))
            {
                logger.LogError("Trigger {TriggerId} has no cron expression in config, disabling", trigger.Id);
                return null;
            }

            return CronExpression.Parse(config.Cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trigger {TriggerId} has invalid cron config, disabling", trigger.Id);
            return null;
        }
    }
}
