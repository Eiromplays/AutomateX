using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Durable wait: a wait step suspends the execution (Waiting) until a timer or an external
// ResumeExecution wakes it; the run then continues. The resume payload is the wait step's output.
public sealed class DurableWaitEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedAsync(params StepDefinition[] steps)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"wait-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(steps);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Delay_wait_suspends_then_resumes_on_the_timer()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            new StepDefinition("test.probe", "before", "{}"),
            new StepDefinition("wait", "pause", """{"delaySeconds":0}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var execution = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        var wait = execution.Steps.Single(s => s.StepOrder == 1);
        Assert.Equal(ExecutionStatus.Succeeded, wait.Status);
        Assert.Contains("\"reason\":\"timer\"", wait.Output);
        Assert.Equal(2, fixture.ProbeAction.Calls); // before + after
    }

    [Fact]
    public async Task Signal_wait_stays_waiting_until_resumed_then_uses_the_payload()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            new StepDefinition("test.probe", "before", "{}"),
            new StepDefinition("wait", "approval", """{"mode":"signal"}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);

        // It parks at the wait and stays there — the "after" step has not run.
        await TestData.WaitForStatusAsync(fixture.Host, executionId, ExecutionStatus.Waiting, Timeout);
        Assert.Equal(1, fixture.ProbeAction.Calls);

        await TestData.PublishAsync(
            fixture.Host, new ResumeExecution(executionId, 1, "resumed", """{"decision":"approve"}"""));

        var execution = await TestData.WaitForCompletedAsync(fixture.Host, executionId, Timeout);
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Contains("approve", execution.Steps.Single(s => s.StepOrder == 1).Output);
        Assert.Equal(2, fixture.ProbeAction.Calls);
    }

    [Fact]
    public async Task Signal_wait_resumes_on_timeout()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            new StepDefinition("test.probe", "before", "{}"),
            new StepDefinition("wait", "approval", """{"mode":"signal","timeoutSeconds":0}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var execution = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Contains("\"reason\":\"timeout\"", execution.Steps.Single(s => s.StepOrder == 1).Output);
    }

    [Fact]
    public async Task Resuming_an_already_resumed_execution_is_a_no_op()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync(
            new StepDefinition("wait", "approval", """{"mode":"signal"}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        await TestData.WaitForStatusAsync(fixture.Host, executionId, ExecutionStatus.Waiting, Timeout);

        await TestData.PublishAsync(fixture.Host, new ResumeExecution(executionId, 0, "resumed", null));
        await TestData.WaitForCompletedAsync(fixture.Host, executionId, Timeout);
        // A second resume after completion changes nothing.
        await TestData.PublishAsync(fixture.Host, new ResumeExecution(executionId, 0, "resumed", null));
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(1, fixture.ProbeAction.Calls); // "after" ran exactly once
    }
}
