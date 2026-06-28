using AutomateX.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine;

// Prunes append-only / cache tables past their configured retention windows. Both windows are opt-in
// (null = keep forever); executions have their own retention in StuckExecutionSweeper.
public sealed class RetentionSweeper(
    IServiceProvider serviceProvider,
    IOptions<EngineOptions> engineOptions,
    ILogger<RetentionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (engineOptions.Value is { AuditRetention: null, IdempotencyRetention: null })
        {
            return; // nothing to prune
        }

        using var timer = new PeriodicTimer(engineOptions.Value.SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
                var now = DateTimeOffset.UtcNow;

                if (engineOptions.Value.AuditRetention is { } auditRetention)
                {
                    var deleted = await PruneAuditAsync(dbContext, now - auditRetention, stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Pruned {Count} audit entries past the {Retention} window", deleted, auditRetention);
                    }
                }

                if (engineOptions.Value.IdempotencyRetention is { } idempotencyRetention)
                {
                    var deleted = await PruneIdempotencyAsync(dbContext, now - idempotencyRetention, stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation(
                            "Pruned {Count} idempotency records past the {Retention} window", deleted, idempotencyRetention);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention sweep failed");
            }
        }
    }

    public static Task<int> PruneAuditAsync(AutomateXDbContext dbContext, DateTimeOffset olderThan, CancellationToken ct = default) =>
        dbContext.AuditEntries.Where(x => x.At < olderThan).ExecuteDeleteAsync(ct);

    public static Task<int> PruneIdempotencyAsync(AutomateXDbContext dbContext, DateTimeOffset olderThan, CancellationToken ct = default) =>
        dbContext.IdempotencyRecords.Where(x => x.CreatedAt < olderThan).ExecuteDeleteAsync(ct);
}
