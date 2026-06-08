using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// schedule.workflow exposes Wolverine's durable scheduling: a step schedules a
// target workflow to run later with a payload. The scheduled run survives like
// any durable message and arrives carrying triggeredBy "scheduled".
public sealed class ScheduledWorkflowTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(30);

    private async Task<Guid> SeedSchedulerAsync(Guid targetWorkflowId, int delaySeconds)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"scheduler-{Guid.CreateVersion7():N}", null);
        var config = $$"""
            {"workflowId":"{{targetWorkflowId}}","delaySeconds":{{delaySeconds}},"payload":"{\"reminder\":\"call mum\"}"}
            """;
        workflow.AddVersion([new StepDefinition("schedule.workflow", "schedule it", config)]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task A_step_schedules_a_future_run_of_the_target_workflow()
    {
        fixture.ProbeAction.Reset();
        var target = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var scheduler = await SeedSchedulerAsync(target, delaySeconds: 1);

        var schedulerExecution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, scheduler), TerminalTimeout);
        Assert.Equal(ExecutionStatus.Succeeded, schedulerExecution.Status);

        // The scheduler step's output carries the scheduled run id + time.
        Assert.Contains("scheduledExecutionId", Assert.Single(schedulerExecution.Steps).Output);

        // The target eventually runs on its own, triggered as "scheduled", with the payload.
        var deadline = DateTimeOffset.UtcNow + TerminalTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var run = await dbContext.Executions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowId == target && x.TriggeredBy == "scheduled");

            if (run is not null)
            {
                Assert.Contains("call mum", run.TriggerPayload);
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("the scheduled workflow never fired");
    }
}
