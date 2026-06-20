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

        // A branching showcase for the builder graph: probe → switch → fan-out (page + log) that
        // rejoins, with continue-on-failure. Built-in actions only, so it needs no plugins.
        var watchdog = Workflow.Create(
            "api-watchdog", "Dev seed: branching demo — switch into parallel lanes that rejoin.");
        watchdog.AddVersion(
            [
                new StepDefinition("http.request", "Probe API",
                    """{"method":"GET","url":"https://api.github.com/zen","failOnErrorStatus":false}"""),
                new StepDefinition("switch", "Healthy?",
                    """{"value":"{{steps.0.output.statusCode}}","cases":[{"label":"up","equals":"200"}]}"""),
                new StepDefinition("http.request", "Heartbeat OK", """{"method":"POST","url":"https://httpbin.org/post"}"""),
                new StepDefinition("http.request", "Diagnose", """{"method":"POST","url":"https://httpbin.org/post"}"""),
                new StepDefinition("http.request", "Page on-call", """{"method":"POST","url":"https://httpbin.org/post"}"""),
                new StepDefinition("http.request", "Log incident", """{"method":"POST","url":"https://httpbin.org/post"}"""),
                new StepDefinition("http.request", "Record outcome", """{"method":"POST","url":"https://httpbin.org/post"}"""),
            ],
            [
                new EdgeDefinition(0, 1, null),
                new EdgeDefinition(1, 2, "up"),
                new EdgeDefinition(1, 3, "default"),
                new EdgeDefinition(3, 4, null),
                new EdgeDefinition(3, 5, null),
                new EdgeDefinition(4, 6, null),
                new EdgeDefinition(5, 6, null),
            ],
            continueOnFailure: true);
        dbContext.Workflows.Add(watchdog);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
