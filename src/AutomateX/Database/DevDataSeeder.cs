using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using Cronos;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Database;

public static class DevDataSeeder
{
    public static async Task SeedAsync(AutomateXDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Workflows.AnyAsync(cancellationToken))
        {
            return;
        }

        var workflow = Workflow.Create("heartbeat", "Dev seed: fetches GitHub zen once per minute.");
        workflow.AddVersion([
            new StepDefinition("http.request", "Fetch zen", """{"method":"GET","url":"https://api.github.com/zen"}"""),
        ]);
        dbContext.Workflows.Add(workflow);

        var trigger = Trigger.Create(
            workflow.Id,
            TriggerTypes.Cron,
            """{"cron":"* * * * *"}""",
            CronExpression.Parse("* * * * *").GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
        dbContext.Triggers.Add(trigger);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
