using Cronos;
using Wolverine;

namespace AutomateX.Engine;

public sealed class CronTriggerWorker(
    IServiceProvider serviceProvider,
    ILogger<CronTriggerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cron = CronExpression.Parse(HardcodedWorkflow.CronExpression);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            if (next is null)
            {
                logger.LogWarning(
                    "Cron expression {Expression} has no future occurrences, stopping trigger",
                    HardcodedWorkflow.CronExpression);
                return;
            }

            var delay = next.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            await using var scope = serviceProvider.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new RunWorkflow(HardcodedWorkflow.Id, "cron"));
        }
    }
}
