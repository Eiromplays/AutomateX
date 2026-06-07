using AutomateX.Database;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The plugin-delete guard: a plugin is "in use" when any workflow's LATEST version
// references one of its action types. History is pinned and doesn't count — only
// code paths that future executions would take.
public sealed class PluginUsageTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private async Task<AutomateXDbContext> DbAsync()
    {
        var scope = fixture.Host.Services.CreateAsyncScope();
        return await Task.FromResult(scope.ServiceProvider.GetRequiredService<AutomateXDbContext>());
    }

    [Fact]
    public async Task Workflow_whose_latest_version_uses_the_action_blocks()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        await using var dbContext = await DbAsync();
        var name = (await dbContext.Workflows.AsNoTracking().FirstAsync(x => x.Id == workflowId)).Name;

        var blocking = await PluginUsage.FindBlockingWorkflowsAsync(
            dbContext, ["test.probe"], workspaceId: null, CancellationToken.None);

        Assert.Contains(name, blocking);
    }

    [Fact]
    public async Task Only_the_latest_version_counts()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        await using var dbContext = await DbAsync();
        var workflow = await dbContext.Workflows.Include(x => x.Versions).FirstAsync(x => x.Id == workflowId);
        var version = workflow.AddVersion([new StepDefinition("http.request", null, "{}")]);
        dbContext.WorkflowVersions.Add(version);
        await dbContext.SaveChangesAsync();

        var blocking = await PluginUsage.FindBlockingWorkflowsAsync(
            dbContext, ["test.probe"], workspaceId: null, CancellationToken.None);

        Assert.DoesNotContain(workflow.Name, blocking);
    }

    [Fact]
    public async Task Workspace_scoped_lookup_ignores_other_workspaces()
    {
        await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1); // lands in Default

        await using var dbContext = await DbAsync();
        var blocking = await PluginUsage.FindBlockingWorkflowsAsync(
            dbContext, ["test.probe"], workspaceId: Guid.CreateVersion7(), CancellationToken.None);

        Assert.Empty(blocking);
    }

    [Fact]
    public async Task No_action_types_means_nothing_blocks()
    {
        await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        await using var dbContext = await DbAsync();
        var blocking = await PluginUsage.FindBlockingWorkflowsAsync(
            dbContext, [], workspaceId: Workspace.DefaultId, CancellationToken.None);

        Assert.Empty(blocking);
    }
}
