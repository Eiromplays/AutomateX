using System.Text.Json;
using AutomateX.Database;
using AutomateX.Modules.Triggers;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AutomateX.Engine;

public sealed class CronScheduler(
    IServiceProvider serviceProvider,
    IOptions<EngineOptions> engineOptions,
    ILogger<CronScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(engineOptions.Value.CronPollInterval);

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
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<AutomateXDbContext>>();
        var dbContext = outbox.DbContext;

        // Single-statement lease claim: due triggers are pushed 5 minutes out atomically, so a
        // concurrent node (or a crash before the real reschedule below) can't double-fire — worst
        // case a fire is delayed by the lease, never duplicated.
        var due = await dbContext.Triggers
            .FromSqlRaw("""
                UPDATE "Triggers"
                SET "NextRunAt" = now() + interval '5 minutes'
                WHERE "Type" = 'cron' AND "Enabled" AND "NextRunAt" <= now()
                RETURNING *
                """)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return;
        }

        foreach (var trigger in due)
        {
            var executionId = Guid.CreateVersion7();
            await outbox.PublishAsync(new RunWorkflow(executionId, trigger.WorkflowId, $"cron:{trigger.Id}"));
            trigger.MarkFired(ComputeNextRunAt(trigger));
        }

        // Outbox: the real NextRunAt values and the outgoing RunWorkflow envelopes commit atomically.
        await outbox.SaveChangesAndFlushMessagesAsync();
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
