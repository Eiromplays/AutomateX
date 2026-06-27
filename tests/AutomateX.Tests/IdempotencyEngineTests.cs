using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// A step with an idempotency key runs its action at most once per resolved key: the first success is
// cached and returned on later runs without re-invoking. Failures aren't cached; an unkeyed step runs
// every time.
public sealed class IdempotencyEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedAsync(string? idempotencyKey)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"idem-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion([new StepDefinition("test.probe", "p", "{}", IdempotencyKey: idempotencyKey)]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Execution> RunAsync(Guid workflowId, string? payload = null)
    {
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId, payload);
        return await TestData.WaitForCompletedAsync(fixture.Host, executionId, Timeout);
    }

    [Fact]
    public async Task Keyed_step_caches_first_result_and_skips_re_invoke()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync("static-key");

        var first = await RunAsync(workflowId);
        var second = await RunAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, first.Status);
        Assert.Equal(ExecutionStatus.Succeeded, second.Status);
        Assert.Equal(1, fixture.ProbeAction.Calls); // the action ran once across both executions
        Assert.Equal(
            first.Steps.Single().Output,
            second.Steps.Single().Output); // second returned the cached result
    }

    [Fact]
    public async Task Templated_key_dedups_same_payload_but_not_different()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync("{{trigger.payload.id}}");

        await RunAsync(workflowId, """{"id":"a"}""");
        await RunAsync(workflowId, """{"id":"a"}"""); // same key → cached
        await RunAsync(workflowId, """{"id":"b"}"""); // new key → runs

        Assert.Equal(2, fixture.ProbeAction.Calls);
    }

    [Fact]
    public async Task Failed_keyed_step_is_not_cached()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var workflowId = await SeedAsync("static-key");

        var run = await RunAsync(workflowId);
        Assert.Equal(ExecutionStatus.Failed, run.Status);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        Assert.False(
            await dbContext.IdempotencyRecords.AnyAsync(x => x.WorkflowId == workflowId),
            "a failed action must not be cached");
    }

    [Fact]
    public async Task Unkeyed_step_runs_every_time()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(idempotencyKey: null);

        await RunAsync(workflowId);
        await RunAsync(workflowId);

        Assert.Equal(2, fixture.ProbeAction.Calls);
    }
}
