using AutomateX.Modules.Executions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// A trigger's entry step (RunWorkflow.EntryOrder) starts the run mid-workflow: the chosen step and
// everything after it run, the earlier steps never do. A null/out-of-range entry is the first step.
public sealed class TriggerEntryEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Entry_order_starts_the_run_at_that_step()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, 3); // steps 0, 1, 2

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, payload: null, entryOrder: 1), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // steps 1 and 2 only

        var orders = execution.Steps.Select(s => s.StepOrder).OrderBy(o => o).ToList();
        Assert.Equal([1, 2], orders); // step 0 never ran — no row for it
    }

    [Fact]
    public async Task Out_of_range_entry_falls_back_to_the_first_step()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, 2);

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, payload: null, entryOrder: 99), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // ran from step 0 as usual

        var orders = execution.Steps.Select(s => s.StepOrder).OrderBy(o => o).ToList();
        Assert.Equal([0, 1], orders);
    }
}
