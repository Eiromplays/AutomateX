using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace AutomateX.Tests;

internal static class TestData
{
    public static Task<Guid> SeedWorkflowAsync(IHost host, int stepCount) =>
        SeedWorkflowAsync(host, Enumerable.Repeat("{}", stepCount).ToList());

    public static async Task<Guid> SeedWorkflowAsync(IHost host, IReadOnlyList<string> stepConfigs)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workflow = Workflow.Create($"test-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(stepConfigs
            .Select((config, i) => new StepDefinition("test.probe", $"step {i}", config))
            .ToList());

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    public static async Task<Guid> ExecuteAsync(
        IHost host, Guid workflowId, string? payload = null, int? entryOrder = null)
    {
        var executionId = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new RunWorkflow(executionId, workflowId, "test", payload, entryOrder));

        return executionId;
    }

    public static async Task<Execution> WaitForTerminalAsync(IHost host, Guid executionId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = host.Services.CreateAsyncScope();
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

        throw new TimeoutException($"Execution {executionId} did not reach a terminal state within {timeout}.");
    }
}
