using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Pins the runtime behaviour of the API-uptime-watchdog example's shape: a switch that either runs
// a quiet heartbeat or falls through to a diagnose step that fans out into two lanes (page + log)
// which join — including continue-on-failure, where a failed log lane still lets the page fire.
public sealed class WatchdogTopologyEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    // 0 switch(status) → "up": 1 heartbeat (terminal); default: 2 diagnose ⇉ 3 page, 4 log → 5 incident.
    // The "log" lane is test.fail, so the down path exercises continue-on-failure.
    private async Task<Guid> SeedAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"watchdog-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
            [
                new StepDefinition("switch", "status", """{"value":"{{trigger.payload.status}}","cases":[{"label":"up","equals":"200"}]}"""),
                new StepDefinition("test.probe", "heartbeat", """{"lane":"heartbeat"}"""),
                new StepDefinition("test.probe", "diagnose", """{"lane":"diagnose"}"""),
                new StepDefinition("test.probe", "page", """{"lane":"page"}"""),
                new StepDefinition("test.fail", "log", "{}"),
                new StepDefinition("test.probe", "incident", """{"lane":"incident"}"""),
            ],
            [
                new EdgeDefinition(0, 1, "up"),
                new EdgeDefinition(0, 2, "default"),
                new EdgeDefinition(2, 3, null),
                new EdgeDefinition(2, 4, null),
                new EdgeDefinition(3, 5, null),
                new EdgeDefinition(4, 5, null),
            ],
            continueOnFailure: true);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Healthy_status_runs_only_the_heartbeat()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, """{"status":"200"}"""), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(1, fixture.ProbeAction.Calls); // heartbeat only — the incident subtree is skipped

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // switch
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status); // heartbeat
        Assert.All(steps.Skip(2), s => Assert.Equal(ExecutionStatus.Skipped, s.Status));
    }

    [Fact]
    public async Task Down_status_pages_even_when_the_log_lane_fails()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, """{"status":"503"}"""), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // diagnose + page (heartbeat skipped, log failed)

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // switch
        Assert.Equal(ExecutionStatus.Skipped, steps[1].Status);   // heartbeat
        Assert.Equal(ExecutionStatus.Succeeded, steps[2].Status); // diagnose
        Assert.Equal(ExecutionStatus.Succeeded, steps[3].Status); // page — fired despite the failing lane
        Assert.Equal(ExecutionStatus.Failed, steps[4].Status);    // log
        Assert.Equal(ExecutionStatus.Skipped, steps[5].Status);   // incident — a failed input blocks the join
    }
}
