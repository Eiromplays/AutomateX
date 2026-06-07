using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Workflow chaining: a "workflow" trigger fires its workflow when the watched
// workflow's execution reaches a terminal state. Chained messages ride the same
// outbox as step cascades; depth is capped; workspaces are never crossed.
public sealed class WorkflowChainingTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    private async Task AddChainTriggerAsync(Guid onWorkflow, Guid watched, string on)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        dbContext.Triggers.Add(Trigger.Create(
            onWorkflow, "workflow", $$"""{"workflowId":"{{watched}}","on":"{{on}}"}""", null));
        await dbContext.SaveChangesAsync();
    }

    private async Task<Execution?> WaitForChainedAsync(Guid workflowId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var execution = await dbContext.Executions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowId == workflowId);
            if (execution is not null)
            {
                return execution;
            }

            await Task.Delay(100);
        }

        return null;
    }

    [Fact]
    public async Task Chained_workflow_fires_on_success_with_source_context()
    {
        fixture.ProbeAction.Reset();
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var target = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddChainTriggerAsync(target, watched: source, on: "succeeded");

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        await TestData.WaitForTerminalAsync(fixture.Host, sourceExecutionId, TerminalTimeout);

        var chained = await WaitForChainedAsync(target, TerminalTimeout);
        Assert.NotNull(chained);
        Assert.Equal("workflow", chained.TriggeredBy);

        // Parse, don't substring: jsonb storage canonicalizes key order and spacing.
        var payload = System.Text.Json.Nodes.JsonNode.Parse(chained.TriggerPayload!)!;
        Assert.Equal(1, (int)payload["chainDepth"]!);
        Assert.Equal(sourceExecutionId.ToString(), (string)payload["source"]!["executionId"]!);
        Assert.Equal("Succeeded", (string)payload["source"]!["status"]!);
    }

    [Fact]
    public async Task Failure_triggers_fire_only_on_failure()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var onFailed = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var onSucceeded = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddChainTriggerAsync(onFailed, watched: source, on: "failed");
        await AddChainTriggerAsync(onSucceeded, watched: source, on: "succeeded");

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        var sourceExecution = await TestData.WaitForTerminalAsync(fixture.Host, sourceExecutionId, TerminalTimeout);
        Assert.Equal(ExecutionStatus.Failed, sourceExecution.Status);

        Assert.NotNull(await WaitForChainedAsync(onFailed, TerminalTimeout));
        Assert.Null(await WaitForChainedAsync(onSucceeded, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Self_loops_are_contained_by_the_depth_cap()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddChainTriggerAsync(workflowId, watched: workflowId, on: "any");

        await TestData.ExecuteAsync(fixture.Host, workflowId);

        // Wait until the chain stops growing, then check the total.
        var previous = -1;
        var stableSince = DateTimeOffset.UtcNow;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        int count;
        while (true)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            count = await dbContext.Executions.CountAsync(x => x.WorkflowId == workflowId);

            if (count != previous)
            {
                previous = count;
                stableSince = DateTimeOffset.UtcNow;
            }
            else if (DateTimeOffset.UtcNow - stableSince > TimeSpan.FromSeconds(3))
            {
                break;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, "chain never stabilized");
            await Task.Delay(200);
        }

        var options = fixture.Host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<EngineOptions>>().Value;
        Assert.Equal(options.MaxChainDepth + 1, count); // depth 0 (manual) … MaxChainDepth
    }

    [Fact]
    public async Task Chains_never_cross_workspaces()
    {
        fixture.ProbeAction.Reset();
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1); // Default workspace

        Guid foreignTarget;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var workspace = Workspace.Create("chain-isolation");
            var workflow = Workflow.Create($"foreign-{Guid.CreateVersion7():N}", null, workspace.Id);
            workflow.AddVersion([new StepDefinition("test.probe", null, "{}")]);
            dbContext.Workspaces.Add(workspace);
            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync();
            foreignTarget = workflow.Id;
        }

        await AddChainTriggerAsync(foreignTarget, watched: source, on: "any");

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        await TestData.WaitForTerminalAsync(fixture.Host, sourceExecutionId, TerminalTimeout);

        Assert.Null(await WaitForChainedAsync(foreignTarget, TimeSpan.FromSeconds(2)));
    }
}
