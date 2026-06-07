using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The rules that make "edit workflow" safe to expose:
// editing appends an immutable version; history is pinned to the version it ran.
public sealed class WorkflowLifecycleTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Editing_appends_a_version_and_history_stays_pinned()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, ["""{"marker":"v1"}"""]);
        var first = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var workflow = await dbContext.Workflows.Include(x => x.Versions).FirstAsync(x => x.Id == workflowId);
            var version = workflow.AddVersion([new StepDefinition("test.probe", null, """{"marker":"v2"}""")]);
            dbContext.WorkflowVersions.Add(version);
            await dbContext.SaveChangesAsync();
        }

        var second = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        // New executions pick up the new version…
        Assert.NotEqual(first.WorkflowVersionId, second.WorkflowVersionId);
        Assert.Contains("v2", Assert.Single(second.Steps).Output);

        // …while the old execution still references its version, output untouched.
        var firstAfterEdit = await TestData.WaitForTerminalAsync(fixture.Host, first.Id, TerminalTimeout);
        Assert.Equal(first.WorkflowVersionId, firstAfterEdit.WorkflowVersionId);
        Assert.Contains("v1", Assert.Single(firstAfterEdit.Steps).Output);
    }

    [Fact]
    public async Task Deleting_a_workflow_removes_versions_steps_triggers_and_history()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        dbContext.Triggers.Add(Trigger.Create(workflowId, "cron", """{"cron":"0 0 1 1 *"}""", DateTimeOffset.UtcNow.AddYears(1)));
        await dbContext.SaveChangesAsync();

        var deleted = await WorkflowDeletion.DeleteAsync(dbContext, workflowId, Workspace.DefaultId, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(await dbContext.Workflows.AnyAsync(x => x.Id == workflowId));
        Assert.False(await dbContext.WorkflowVersions.AnyAsync(x => x.WorkflowId == workflowId));
        Assert.False(await dbContext.Triggers.AnyAsync(x => x.WorkflowId == workflowId));
        Assert.False(await dbContext.Executions.AnyAsync(x => x.WorkflowId == workflowId));
        Assert.False(await dbContext.StepExecutions.AnyAsync(x => x.ExecutionId == execution.Id));
    }

    [Fact]
    public async Task Deleting_a_workflow_in_the_wrong_workspace_deletes_nothing()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var deleted = await WorkflowDeletion.DeleteAsync(dbContext, workflowId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.False(deleted);
        Assert.True(await dbContext.Workflows.AnyAsync(x => x.Id == workflowId));
    }

    [Fact]
    public async Task Deleting_an_execution_cascades_step_executions()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 2);
        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), TerminalTimeout);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        await dbContext.Executions.Where(x => x.Id == execution.Id).ExecuteDeleteAsync();

        Assert.False(await dbContext.StepExecutions.AnyAsync(x => x.ExecutionId == execution.Id));
    }
}

public sealed class ExecutionDeleteRulesTests
{
    [Theory]
    [InlineData(ExecutionStatus.Pending, false)]
    [InlineData(ExecutionStatus.Running, false)]
    [InlineData(ExecutionStatus.Succeeded, true)]
    [InlineData(ExecutionStatus.Failed, true)]
    public void Only_terminal_executions_can_be_deleted(ExecutionStatus status, bool expected) =>
        Assert.Equal(expected, ExecutionDeleteRules.CanDelete(status));
}
