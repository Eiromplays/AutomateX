using AutomateX.Database;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine;

// Safety net for executions orphaned outside the engine's normal paths (dead-lettered
// envelopes, lost cascades). The threshold must comfortably exceed the retry ladder.
public sealed class StuckExecutionSweeper(
    IServiceProvider serviceProvider,
    IOptions<EngineOptions> engineOptions,
    ILogger<StuckExecutionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(engineOptions.Value.SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
                var cutoff = DateTimeOffset.UtcNow - engineOptions.Value.StuckExecutionThreshold;

                var swept = await SweepAsync(dbContext, cutoff, stoppingToken);
                if (swept > 0)
                {
                    logger.LogWarning("Marked {Count} stuck execution(s) older than {Cutoff} as failed", swept, cutoff);
                }

                if (engineOptions.Value.ExecutionRetention is { } retention)
                {
                    var deleted = await SweepRetentionAsync(dbContext, DateTimeOffset.UtcNow - retention, stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Deleted {Count} execution(s) past the {Retention} retention window", deleted, retention);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stuck execution sweep failed");
            }
        }
    }

    public static async Task<int> SweepAsync(
        AutomateXDbContext dbContext,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await dbContext.StepExecutions
            .Where(s => s.Status == ExecutionStatus.Running && dbContext.Executions.Any(e =>
                e.Id == s.ExecutionId && e.Status == ExecutionStatus.Running && e.StartedAt < olderThan))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Status, ExecutionStatus.Failed)
                .SetProperty(s => s.Error, "Swept: execution exceeded the stuck-execution threshold")
                .SetProperty(s => s.CompletedAt, DateTimeOffset.UtcNow), cancellationToken);

        return await dbContext.Executions
            .Where(e => e.Status == ExecutionStatus.Running && e.StartedAt < olderThan)
            .ExecuteUpdateAsync(set => set
                .SetProperty(e => e.Status, ExecutionStatus.Failed)
                .SetProperty(e => e.CompletedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    // Retention: terminal executions (and their steps, via cascade) older than the cutoff.
    public static async Task<int> SweepRetentionAsync(
        AutomateXDbContext dbContext,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default) =>
        await dbContext.Executions
            .Where(e => e.Status != ExecutionStatus.Running && e.StartedAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
}
