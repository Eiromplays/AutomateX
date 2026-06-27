using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// execution.onFailure: a failed execution starts every enabled onFailure trigger's workflow in the
// same workspace with a failure summary. Alert runs never re-alert (self-exclusion), sub-workflow
// children are suppressed unless opted in, and an optional watchWorkflowId scopes the subscription.
public sealed class FailureAlertingTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    private async Task AddOnFailureTriggerAsync(Guid alertWorkflow, Guid? watchWorkflowId = null, bool includeSubWorkflows = false)
    {
        var config = new JsonObject();
        if (watchWorkflowId is { } watched)
        {
            config["watchWorkflowId"] = watched.ToString();
        }

        if (includeSubWorkflows)
        {
            config["includeSubWorkflows"] = true;
        }

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        dbContext.Triggers.Add(Trigger.Create(alertWorkflow, TriggerTypes.OnFailure, config.ToJsonString(), null));
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> SeedStepsAsync(params StepDefinition[] steps)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"alert-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(steps.ToList());
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Execution?> WaitForAlertAsync(Guid alertWorkflow, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var execution = await dbContext.Executions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WorkflowId == alertWorkflow && x.TriggeredBy == TriggerTypes.OnFailure);
            if (execution is not null)
            {
                return execution;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private async Task<int> StableCountAsync(Guid alertWorkflow)
    {
        var previous = -1;
        var stableSince = DateTimeOffset.UtcNow;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var count = await dbContext.Executions.CountAsync(x => x.WorkflowId == alertWorkflow);

            if (count != previous)
            {
                previous = count;
                stableSince = DateTimeOffset.UtcNow;
            }
            else if (DateTimeOffset.UtcNow - stableSince > TimeSpan.FromSeconds(3))
            {
                return count;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, "alert count never stabilized");
            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Failed_execution_fires_onFailure_alert_with_context()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert);

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        var sourceExecution = await TestData.WaitForCompletedAsync(fixture.Host, sourceExecutionId, TerminalTimeout);
        Assert.Equal(ExecutionStatus.Failed, sourceExecution.Status);

        var fired = await WaitForAlertAsync(alert, TerminalTimeout);
        Assert.NotNull(fired);

        var payload = JsonNode.Parse(fired.TriggerPayload!)!;
        Assert.Equal(source.ToString(), (string)payload["workflowId"]!);
        Assert.Equal(sourceExecutionId.ToString(), (string)payload["executionId"]!);
        Assert.Equal("Failed", (string)payload["status"]!);
        Assert.False(string.IsNullOrEmpty((string?)payload["failedStep"]!["error"]));
    }

    [Fact]
    public async Task Successful_execution_does_not_alert()
    {
        fixture.ProbeAction.Reset();
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert);

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        var sourceExecution = await TestData.WaitForCompletedAsync(fixture.Host, sourceExecutionId, TerminalTimeout);
        Assert.Equal(ExecutionStatus.Succeeded, sourceExecution.Status);

        Assert.Null(await WaitForAlertAsync(alert, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Alert_runs_do_not_re_alert()
    {
        // Everything fails: the source fires the alert once, and the alert's own failure is
        // self-excluded (TriggeredBy == execution.onFailure) — so the alert workflow runs exactly once.
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var source = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert);

        var sourceExecutionId = await TestData.ExecuteAsync(fixture.Host, source);
        // Wait through the source's retries/backoff and for the alert to fire once before counting —
        // otherwise StableCountAsync can settle on 0 before the source has even failed.
        await TestData.WaitForCompletedAsync(fixture.Host, sourceExecutionId, TerminalTimeout);
        Assert.NotNull(await WaitForAlertAsync(alert, TerminalTimeout));

        Assert.Equal(1, await StableCountAsync(alert));
    }

    [Fact]
    public async Task WatchWorkflowId_scopes_alerts_to_one_source()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var watched = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var other = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert, watchWorkflowId: watched);

        var watchedExecutionId = await TestData.ExecuteAsync(fixture.Host, watched);
        await TestData.ExecuteAsync(fixture.Host, other);

        var fired = await WaitForAlertAsync(alert, TerminalTimeout);
        Assert.NotNull(fired);
        Assert.Equal(watched.ToString(), (string)JsonNode.Parse(fired.TriggerPayload!)!["workflowId"]!);
        Assert.Equal(1, await StableCountAsync(alert)); // never fired for `other`
        Assert.Equal(watchedExecutionId.ToString(), (string)JsonNode.Parse(fired.TriggerPayload!)!["executionId"]!);
    }

    [Fact]
    public async Task Sub_workflow_child_failure_is_suppressed_by_default()
    {
        // The child (depth 1) fails, but a failed child returns data to the parent, whose call step
        // succeeds — so neither the parent (succeeded) nor the child (suppressed) alerts.
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var child = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var parent = await SeedStepsAsync(new StepDefinition("workflow.call", "call", $$"""{"workflowId":"{{child}}"}"""));
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert);

        var parentExecutionId = await TestData.ExecuteAsync(fixture.Host, parent);
        await TestData.WaitForCompletedAsync(fixture.Host, parentExecutionId, TerminalTimeout);

        Assert.Null(await WaitForAlertAsync(alert, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task IncludeSubWorkflows_alerts_on_child_failure()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var child = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var parent = await SeedStepsAsync(new StepDefinition("workflow.call", "call", $$"""{"workflowId":"{{child}}"}"""));
        var alert = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        await AddOnFailureTriggerAsync(alert, includeSubWorkflows: true);

        await TestData.ExecuteAsync(fixture.Host, parent);

        var fired = await WaitForAlertAsync(alert, TerminalTimeout);
        Assert.NotNull(fired);
        Assert.Equal(child.ToString(), (string)JsonNode.Parse(fired.TriggerPayload!)!["workflowId"]!);
    }
}
