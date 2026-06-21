using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Error edges (label "error"): a step that fails after its retries routes down its error lane
// instead of halting. A caught failure is recorded Caught and does NOT fail the execution — the
// run settles on the error lane's outcome. Error handling wins over halt and continue-on-failure.
public sealed class ErrorBranchEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedAsync(
        IReadOnlyList<StepDefinition> steps, IReadOnlyList<EdgeDefinition> edges, bool continueOnFailure = false)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"errlane-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(steps, edges, continueOnFailure);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Execution> RunAsync(Guid workflowId) =>
        await TestData.WaitForTerminalAsync(fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

    [Fact]
    public async Task Caught_failure_runs_the_error_lane_and_succeeds()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            [
                new StepDefinition("test.fail", "boom", "{}"),
                new StepDefinition("test.probe", "handle", """{"lane":"error"}"""),
            ],
            [new EdgeDefinition(0, 1, "error")]);

        var execution = await RunAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Caught, steps[0].Status);
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status);
        Assert.Equal(1, fixture.ProbeAction.Calls); // the error handler ran exactly once
    }

    [Fact]
    public async Task Unhandled_failure_on_the_error_lane_fails_the_execution()
    {
        var workflowId = await SeedAsync(
            [
                new StepDefinition("test.fail", "boom", "{}"),
                new StepDefinition("test.fail", "handler-also-fails", "{}"),
            ],
            [new EdgeDefinition(0, 1, "error")]);

        var execution = await RunAsync(workflowId);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Caught, steps[0].Status);
        Assert.Equal(ExecutionStatus.Failed, steps[1].Status);
    }

    [Fact]
    public async Task Error_edge_takes_precedence_over_continue_on_failure()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            [
                new StepDefinition("test.fail", "boom", "{}"),
                new StepDefinition("test.probe", "handle", """{"lane":"error"}"""),
            ],
            [new EdgeDefinition(0, 1, "error")],
            continueOnFailure: true);

        var execution = await RunAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(ExecutionStatus.Caught, execution.Steps.Single(s => s.StepOrder == 0).Status);
    }

    [Fact]
    public async Task Success_skips_the_error_lane()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            [
                new StepDefinition("test.probe", "ok", """{"lane":"main"}"""),
                new StepDefinition("test.probe", "next", """{"lane":"next"}"""),
                new StepDefinition("test.probe", "handle", """{"lane":"error"}"""),
            ],
            [new EdgeDefinition(0, 1, null), new EdgeDefinition(0, 2, "error")]);

        var execution = await RunAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status);
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status);
        Assert.Equal(ExecutionStatus.Skipped, steps[2].Status); // error lane untouched on success
    }
}
