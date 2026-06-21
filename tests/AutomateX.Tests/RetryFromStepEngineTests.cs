using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Retry-from-step: a new execution starts at the chosen step, reusing the source run's upstream
// outputs (seeded, not re-run), pinned to the source's version.
public sealed class RetryFromStepEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private async Task<Guid> SeedAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"retryfrom-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(
        [
            new StepDefinition("test.probe", "a", """{"s":"a"}"""),
            new StepDefinition("test.probe", "b", """{"s":"b"}"""),
            new StepDefinition("test.probe", "c", """{"s":"c"}"""),
        ]);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Reruns_from_the_step_and_reuses_upstream_outputs()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await SeedAsync();

        var original = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, workflowId), Timeout);
        Assert.Equal(ExecutionStatus.Succeeded, original.Status);
        Assert.Equal(3, fixture.ProbeAction.Calls);
        var originalStepA = original.Steps.Single(s => s.StepOrder == 0).Output;

        // Retry from step 1: step 0 is seeded from the original, steps 1 and 2 re-run.
        fixture.ProbeAction.Reset();
        var newId = Guid.CreateVersion7();
        await TestData.PublishAsync(fixture.Host, new RetryFromStep(newId, original.Id, 1));

        var retried = await TestData.WaitForCompletedAsync(fixture.Host, newId, Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, retried.Status);
        Assert.Equal(2, fixture.ProbeAction.Calls); // only b and c ran
        var steps = retried.Steps.OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(3, steps.Count);
        Assert.Equal(ExecutionStatus.Succeeded, steps[0].Status);
        Assert.Equal(originalStepA, steps[0].Output); // step 0 reused, not re-run
    }
}
