using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Security;
using AutomateX.Modules.Connections;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Xunit;

namespace AutomateX.Tests;

public sealed class EngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Workflow_runs_to_success_with_step_outputs()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedWorkflowAsync(stepCount: 2);

        var execution = await RunToTerminalAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(2, execution.Steps.Count);
        Assert.All(execution.Steps, step =>
        {
            Assert.Equal(ExecutionStatus.Succeeded, step.Status);
            Assert.StartsWith("ok:", step.Output);
            Assert.Equal(1, step.Attempts);
        });
        Assert.Equal([0, 1], execution.Steps.OrderBy(x => x.StepOrder).Select(x => x.StepOrder));
    }

    [Fact]
    public async Task Step_retries_then_succeeds()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 2);
        var workflowId = await SeedWorkflowAsync(stepCount: 1);

        var execution = await RunToTerminalAsync(workflowId);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        var step = Assert.Single(execution.Steps);
        // Rule: Attempts = completed invocations (success included), so it equals the probe's call count.
        Assert.Equal(3, step.Attempts);
        Assert.Equal(3, fixture.ProbeAction.Calls);
        Assert.Null(step.Error);
    }

    [Fact]
    public async Task Step_fails_after_max_attempts()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: int.MaxValue);
        var workflowId = await SeedWorkflowAsync(stepCount: 1);

        var execution = await RunToTerminalAsync(workflowId);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        var step = Assert.Single(execution.Steps);
        Assert.Equal(ExecutionStatus.Failed, step.Status);
        Assert.Equal(3, step.Attempts);
        Assert.Contains("probe failure", step.Error);
    }

    [Fact]
    public async Task Data_flows_from_trigger_payload_and_prior_step_outputs()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, [
            "{}",
            """{"prev":"{{steps.0.output}}","name":"{{trigger.payload.name}}"}""",
        ]);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId, """{"name":"eirik"}""");
        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        var secondConfig = fixture.ProbeAction.ReceivedConfigs.ToArray()[1];
        // Probe outputs "ok:<call>:<config>", so step 0's full output starts with "ok:1:".
        Assert.Contains("""
            "prev":"ok:1:
            """.Trim(), secondConfig);
        Assert.Contains("""
            "name":"eirik"
            """.Trim(), secondConfig);
    }

    [Fact]
    public async Task Template_errors_fail_immediately_without_invoking_the_action()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, [
            """{"x":"{{steps.9.output}}"}""",
        ]);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, TerminalTimeout);

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        var step = Assert.Single(execution.Steps);
        Assert.Equal(ExecutionStatus.Failed, step.Status);
        Assert.Equal(0, step.Attempts);
        Assert.Equal(0, fixture.ProbeAction.Calls);
        Assert.Contains("could not be resolved", step.Error);
    }

    [Fact]
    public async Task Connection_secrets_flow_into_configs()
    {
        fixture.ProbeAction.Reset();
        var connectionName = $"testconn-{Guid.CreateVersion7():N}";

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<SecretCipher>();
            dbContext.Connections.Add(Connection.Create(
                connectionName,
                "test",
                cipher.Encrypt("""{"token":"s3cret-token"}""")));
            await dbContext.SaveChangesAsync();
        }

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, [
            "{\"auth\":\"Bearer {{connections." + connectionName + ".token}}\"}",
        ]);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Contains("Bearer s3cret-token", Assert.Single(fixture.ProbeAction.ReceivedConfigs));
    }

    [Fact]
    public async Task Connection_secrets_are_masked_in_outputs_and_events()
    {
        fixture.ProbeAction.Reset();
        fixture.EventListener.Reset();
        var connectionName = $"maskconn-{Guid.CreateVersion7():N}";

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<SecretCipher>();
            dbContext.Connections.Add(Connection.Create(
                connectionName, null, cipher.Encrypt("""{"token":"sup3r-s3cret"}""")));
            await dbContext.SaveChangesAsync();
        }

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, [
            "{\"auth\":\"Bearer {{connections." + connectionName + ".token}}\"}",
        ]);

        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, TerminalTimeout);

        // The action itself received the real value...
        Assert.Contains("sup3r-s3cret", Assert.Single(fixture.ProbeAction.ReceivedConfigs));

        // ...but everything persisted or published is masked.
        var step = Assert.Single(execution.Steps);
        Assert.Equal(ExecutionStatus.Succeeded, step.Status);
        Assert.DoesNotContain("sup3r-s3cret", step.Output);
        Assert.Contains("***", step.Output);

        var stepCompleted = fixture.EventListener.Events
            .OfType<StepCompleted>()
            .Single(x => x.ExecutionId == executionId);
        Assert.DoesNotContain("sup3r-s3cret", stepCompleted.Output);
    }

    [Fact]
    public async Task Retention_sweep_deletes_completed_but_keeps_running()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var completed = Execution.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "test");
        completed.Complete();
        var running = Execution.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "test");
        dbContext.Executions.AddRange(completed, running);
        await dbContext.SaveChangesAsync();

        // Cutoff in the future = every non-running execution is "old".
        await StuckExecutionSweeper.SweepRetentionAsync(dbContext, DateTimeOffset.UtcNow.AddMinutes(1));

        dbContext.ChangeTracker.Clear();
        Assert.Null(await dbContext.Executions.FindAsync(completed.Id));
        Assert.NotNull(await dbContext.Executions.FindAsync(running.Id));
    }

    [Fact]
    public async Task Sweeper_fails_stuck_executions()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var stuck = Execution.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "test");
        var step = stuck.AddStep("test.probe", 0);
        dbContext.Executions.Add(stuck);
        await dbContext.SaveChangesAsync();

        // Cutoff in the future = everything Running is "stuck".
        var swept = await StuckExecutionSweeper.SweepAsync(dbContext, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True(swept >= 1);
        var reloaded = await LoadExecutionAsync(dbContext, stuck.Id);
        Assert.Equal(ExecutionStatus.Failed, reloaded.Status);
        Assert.Equal(ExecutionStatus.Failed, Assert.Single(reloaded.Steps).Status);
    }

    private async Task<Guid> SeedWorkflowAsync(int stepCount)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"test-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(Enumerable.Range(0, stepCount)
            .Select(i => new StepDefinition("test.probe", $"step {i}", "{}"))
            .ToList());

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Execution> RunToTerminalAsync(Guid workflowId)
    {
        var executionId = Guid.CreateVersion7();

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new RunWorkflow(executionId, workflowId, "test"));
        }

        var deadline = DateTimeOffset.UtcNow + TerminalTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var execution = await dbContext.Executions
                .AsNoTracking()
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == executionId);

            if (execution is not null && execution.Status is not ExecutionStatus.Running)
            {
                return execution;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Execution {executionId} did not reach a terminal state within {TerminalTimeout}.");
    }

    private static async Task<Execution> LoadExecutionAsync(AutomateXDbContext dbContext, Guid id)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.Executions
            .AsNoTracking()
            .Include(x => x.Steps)
            .FirstAsync(x => x.Id == id);
    }
}
