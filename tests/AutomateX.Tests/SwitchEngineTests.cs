using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// A switch routes the run down the edge whose label its output chose; steps reachable
// only via a not-taken edge are recorded Skipped, and the execution still Succeeds.
public sealed class SwitchEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    // 0=switch on {{trigger.payload.x}} — "go" → "a"; anything else → default.
    //   edge "a"      : 0 → 1 (lane-a, terminal)
    //   edge "default": 0 → 2 (lane-d) → 3 (lane-d2)
    private async Task<Guid> SeedBranchedAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"switch-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
            [
                new StepDefinition("switch", "switch", """{"value":"{{trigger.payload.x}}","cases":[{"label":"a","equals":"go"}]}"""),
                new StepDefinition("test.probe", "lane-a", """{"lane":"a"}"""),
                new StepDefinition("test.probe", "lane-d", """{"lane":"d"}"""),
                new StepDefinition("test.probe", "lane-d2", """{"lane":"d2"}"""),
            ],
            [
                new EdgeDefinition(0, 1, "a"),
                new EdgeDefinition(0, 2, "default"),
                new EdgeDefinition(2, 3, null),
            ]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Matching_case_runs_its_lane_and_skips_the_other()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedBranchedAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, """{"x":"go"}"""), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(1, fixture.ProbeAction.Calls); // only lane-a ran
        Assert.Contains(fixture.ProbeAction.ReceivedConfigs, c => c.Contains("\"lane\":\"a\"", StringComparison.Ordinal));

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // switch
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status); // lane-a
        Assert.Equal(ExecutionStatus.Skipped, steps[2].Status);   // lane-d
        Assert.Equal(ExecutionStatus.Skipped, steps[3].Status);   // lane-d2
    }

    // A diamond: switch → lane-a / default, both re-converging on a merge step. The taken lane
    // runs through to the merge; the other lane is Skipped; the merge runs exactly once. This is
    // "Phase 2" merge, which the reachability engine already handles for conditional forks.
    private async Task<Guid> SeedDiamondAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"diamond-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
            [
                new StepDefinition("switch", "switch", """{"value":"{{trigger.payload.x}}","cases":[{"label":"a","equals":"go"}]}"""),
                new StepDefinition("test.probe", "lane-a", """{"lane":"a"}"""),
                new StepDefinition("test.probe", "lane-d", """{"lane":"d"}"""),
                new StepDefinition("test.probe", "merge", """{"lane":"merge"}"""),
            ],
            [
                new EdgeDefinition(0, 1, "a"),
                new EdgeDefinition(0, 2, "default"),
                new EdgeDefinition(1, 3, null),
                new EdgeDefinition(2, 3, null),
            ]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Conditional_diamond_merges_on_one_lane()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedDiamondAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, """{"x":"go"}"""), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // lane-a + merge, once each

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // switch
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status); // lane-a
        Assert.Equal(ExecutionStatus.Skipped, steps[2].Status);   // lane-d
        Assert.Equal(ExecutionStatus.Succeeded, steps[3].Status); // merge — ran exactly once
    }

    [Fact]
    public async Task No_match_falls_through_to_the_default_lane()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedBranchedAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId, """{"x":"stop"}"""), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // lane-d + lane-d2

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // switch
        Assert.Equal(ExecutionStatus.Skipped, steps[1].Status);   // lane-a skipped
        Assert.Equal(ExecutionStatus.Succeeded, steps[2].Status); // lane-d
        Assert.Equal(ExecutionStatus.Succeeded, steps[3].Status); // lane-d2
    }
}
