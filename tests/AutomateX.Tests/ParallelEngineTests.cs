using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Parallel fan-out + join: a step with two unconditional edges runs both lanes; a join node with
// two incoming edges runs exactly once after both lanes finish; the execution completes only then.
public sealed class ParallelEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    // 0 fork → 1 lane-a and 2 lane-b (both unconditional); 1 → 3 and 2 → 3 (join).
    private async Task<Guid> SeedParallelAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"parallel-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
            [
                new StepDefinition("test.probe", "fork", """{"lane":"fork"}"""),
                new StepDefinition("test.probe", "lane-a", """{"lane":"a"}"""),
                new StepDefinition("test.probe", "lane-b", """{"lane":"b"}"""),
                new StepDefinition("test.probe", "join", """{"lane":"join"}"""),
            ],
            [
                new EdgeDefinition(0, 1, null),
                new EdgeDefinition(0, 2, null),
                new EdgeDefinition(1, 3, null),
                new EdgeDefinition(2, 3, null),
            ]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Fans_out_both_lanes_and_joins_exactly_once()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedParallelAsync();

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(4, fixture.ProbeAction.Calls); // fork, lane-a, lane-b, join — each once

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(4, steps.Count); // the join produced a single row (no double-run)
        Assert.All(steps, s => Assert.Equal(ExecutionStatus.Succeeded, s.Status));

        Assert.Contains(fixture.ProbeAction.ReceivedConfigs, c => c.Contains("\"lane\":\"a\"", StringComparison.Ordinal));
        Assert.Contains(fixture.ProbeAction.ReceivedConfigs, c => c.Contains("\"lane\":\"b\"", StringComparison.Ordinal));
        Assert.Equal(1, fixture.ProbeAction.ReceivedConfigs.Count(c => c.Contains("\"lane\":\"join\"", StringComparison.Ordinal)));
    }

    // 0 fork → 1 lane-a (succeeds) and 2 lane-fail (always fails) → 3 join.
    private async Task<Guid> SeedFailingDiamondAsync(bool continueOnFailure)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"fail-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
            [
                new StepDefinition("test.probe", "fork", """{"lane":"fork"}"""),
                new StepDefinition("test.probe", "lane-a", """{"lane":"a"}"""),
                new StepDefinition("test.fail", "lane-fail", "{}"),
                new StepDefinition("test.probe", "join", """{"lane":"join"}"""),
            ],
            [
                new EdgeDefinition(0, 1, null),
                new EdgeDefinition(0, 2, null),
                new EdgeDefinition(1, 3, null),
                new EdgeDefinition(2, 3, null),
            ],
            continueOnFailure);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Continue_on_failure_lets_the_good_lane_run_and_skips_the_join()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedFailingDiamondAsync(continueOnFailure: true);

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);

        var steps = execution.Steps.OrderBy(x => x.StepOrder).ToList();
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status); // fork
        Assert.Equal(ExecutionStatus.Succeeded, steps[1].Status); // lane-a ran to completion
        Assert.Equal(ExecutionStatus.Failed, steps[2].Status);    // lane-fail
        Assert.Equal(ExecutionStatus.Skipped, steps[3].Status);   // join — a failed input blocks it
    }

    [Fact]
    public async Task Halt_on_failure_fails_the_execution()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedFailingDiamondAsync(continueOnFailure: false);

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Contains(execution.Steps, s => s.StepOrder == 2 && s.Status == ExecutionStatus.Failed);
    }
}
