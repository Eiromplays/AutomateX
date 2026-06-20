using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// A disabled workflow is paused at RunWorkflowHandler, so no run starts no matter the trigger
// (here: a direct dispatch). Re-enabling lets it run again.
public sealed class WorkflowEnabledTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private async Task SetEnabledAsync(Guid workflowId, bool enabled)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = await dbContext.Workflows.FirstAsync(x => x.Id == workflowId);
        workflow.SetEnabled(enabled);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Disabled_workflow_does_not_run()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await SetEnabledAsync(workflowId, false);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);

        // The message is processed and dropped — no execution row is ever created.
        await Task.Delay(TimeSpan.FromSeconds(2));

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        Assert.False(await dbContext.Executions.AnyAsync(x => x.Id == executionId));
    }

    [Fact]
    public async Task Re_enabled_workflow_runs_again()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await SetEnabledAsync(workflowId, false);
        await SetEnabledAsync(workflowId, true);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, TimeSpan.FromSeconds(20));

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
    }
}
