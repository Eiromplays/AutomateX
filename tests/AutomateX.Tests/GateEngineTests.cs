using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// A closed gate halts the workflow cleanly: later steps are Skipped and the
// execution still Succeeds (stopping the chain is normal flow, not failure).
public sealed class GateEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedGatedAsync(string gateConfig)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"gated-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
        [
            new StepDefinition("test.probe", "before", "{}"),
            new StepDefinition("gate", "gate", gateConfig),
            new StepDefinition("test.probe", "after", "{}"),
        ]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Closed_gate_skips_later_steps_and_succeeds()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedGatedAsync("""{"value":"stop","equals":"go"}""");

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(1, fixture.ProbeAction.Calls); // only the step before the gate ran

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // before
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status); // the gate itself
        Assert.Equal(ExecutionStatus.Skipped, steps[2].Status);   // after — never ran
    }

    [Fact]
    public async Task Open_gate_lets_the_workflow_continue()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedGatedAsync("""{"value":"go","equals":"go"}""");

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // both probes ran
        Assert.All(execution.Steps, step => Assert.Equal(ExecutionStatus.Succeeded, step.Status));
    }
}
