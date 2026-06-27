using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// workflow.call runs a child workflow and waits: the parent suspends (Waiting), the child runs, and
// the child's terminal state resumes the parent with the result as the call step's output.
public sealed class SubWorkflowEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedAsync(params StepDefinition[] steps)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"sub-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(steps);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Execution?> ChildOfAsync(Guid parentExecutionId)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        return await dbContext.Executions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ParentExecutionId == parentExecutionId);
    }

    [Fact]
    public async Task Call_runs_the_child_and_resumes_with_its_result()
    {
        fixture.ProbeAction.Reset();
        var childId = await SeedAsync(new StepDefinition("test.probe", "c", """{"k":"child"}"""));
        var parentId = await SeedAsync(
            new StepDefinition("test.probe", "p0", "{}"),
            new StepDefinition("workflow.call", "call", $$"""{"workflowId":"{{childId}}"}"""),
            new StepDefinition("test.probe", "p2", "{}"));

        var parentExecId = await TestData.ExecuteAsync(fixture.Host, parentId);
        var parent = await TestData.WaitForCompletedAsync(fixture.Host, parentExecId, Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        var callStep = parent.Steps.Single(s => s.StepOrder == 1);
        Assert.Equal(ExecutionStatus.Succeeded, callStep.Status);
        Assert.Contains("\"status\":\"Succeeded\"", callStep.Output);
        Assert.Equal(3, fixture.ProbeAction.Calls); // p0 + child c + p2

        var child = await ChildOfAsync(parentExecId);
        Assert.NotNull(child);
        Assert.Equal(ExecutionStatus.Succeeded, child!.Status);
        Assert.Equal(1, child.Depth);
    }

    [Fact]
    public async Task Child_failure_surfaces_as_failed_status_data()
    {
        fixture.ProbeAction.Reset();
        var childId = await SeedAsync(new StepDefinition("test.fail", "boom", "{}"));
        var parentId = await SeedAsync(
            new StepDefinition("workflow.call", "call", $$"""{"workflowId":"{{childId}}"}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var parentExecId = await TestData.ExecuteAsync(fixture.Host, parentId);
        var parent = await TestData.WaitForCompletedAsync(fixture.Host, parentExecId, Timeout);

        // The call step succeeds carrying the child's Failed status as data — the parent continues.
        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        Assert.Contains("\"status\":\"Failed\"", parent.Steps.Single(s => s.StepOrder == 0).Output);
        Assert.Equal(1, fixture.ProbeAction.Calls); // the "after" step ran
    }

    [Fact]
    public async Task Missing_target_fails_the_call_step()
    {
        var parentId = await SeedAsync(
            new StepDefinition("workflow.call", "call", $$"""{"workflowId":"{{Guid.CreateVersion7()}}"}"""));

        var parent = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, parentId), Timeout);

        Assert.Equal(ExecutionStatus.Failed, parent.Status);
        Assert.Equal(ExecutionStatus.Failed, parent.Steps.Single(s => s.StepOrder == 0).Status);
    }
}
