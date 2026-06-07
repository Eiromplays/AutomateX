using AutomateX.Database;
using AutomateX.Modules.Connections;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The connection-delete guard: a connection is "in use" when any workflow's LATEST
// version references {{connections.<name>.…}} in a step config. Same latest-only
// philosophy as the plugin guard — history is pinned, futures matter.
public sealed class ConnectionUsageTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private async Task<(AutomateXDbContext Db, string Name)> SeedAsync(string config)
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, [config]);
        var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var name = (await dbContext.Workflows.AsNoTracking().FirstAsync(x => x.Id == workflowId)).Name;
        return (dbContext, name);
    }

    [Fact]
    public async Task Workflow_referencing_the_connection_blocks()
    {
        var (dbContext, name) = await SeedAsync("""{"token":"{{connections.deploy.privateKey}}"}""");

        var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(
            dbContext, "deploy", Workspace.DefaultId, CancellationToken.None);

        Assert.Contains(name, blocking);
    }

    [Fact]
    public async Task Template_whitespace_is_tolerated()
    {
        var (dbContext, name) = await SeedAsync("""{"token":"{{ connections.spacey.field }}"}""");

        var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(
            dbContext, "spacey", Workspace.DefaultId, CancellationToken.None);

        Assert.Contains(name, blocking);
    }

    [Fact]
    public async Task Name_prefixes_do_not_false_positive()
    {
        var (dbContext, name) = await SeedAsync("""{"token":"{{connections.deployment.key}}"}""");

        var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(
            dbContext, "deploy", Workspace.DefaultId, CancellationToken.None);

        Assert.DoesNotContain(name, blocking);
    }

    [Fact]
    public async Task Only_the_latest_version_counts()
    {
        var (dbContext, name) = await SeedAsync("""{"token":"{{connections.legacy.key}}"}""");

        var workflow = await dbContext.Workflows.Include(x => x.Versions)
            .FirstAsync(x => x.Name == name);
        var version = workflow.AddVersion([new StepDefinition("test.probe", null, "{}")]);
        dbContext.WorkflowVersions.Add(version);
        await dbContext.SaveChangesAsync();

        var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(
            dbContext, "legacy", Workspace.DefaultId, CancellationToken.None);

        Assert.DoesNotContain(name, blocking);
    }

    [Fact]
    public async Task Other_workspaces_are_not_scanned()
    {
        var (dbContext, _) = await SeedAsync("""{"token":"{{connections.scoped.key}}"}""");

        var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(
            dbContext, "scoped", Guid.CreateVersion7(), CancellationToken.None);

        Assert.Empty(blocking);
    }
}
