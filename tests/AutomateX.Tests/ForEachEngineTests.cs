using System.Text.Json;
using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// forEach maps a child workflow over an array (sequential v1): one child run per item, results
// collected in order as the step output, the parent resuming once all items are done.
public sealed class ForEachEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private async Task<Guid> SeedAsync(params StepDefinition[] steps)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var workflow = Workflow.Create($"foreach-{Guid.CreateVersion7():N}", null);
        workflow.AddVersion(steps);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task Maps_a_child_over_each_item_and_collects_results()
    {
        fixture.ProbeAction.Reset();
        var childId = await SeedAsync(new StepDefinition("test.probe", "c", "{}"));
        var parentId = await SeedAsync(
            new StepDefinition("forEach", "loop", $$"""{"items":[1,2,3],"workflowId":"{{childId}}"}"""));

        var parent = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, parentId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        Assert.Equal(3, fixture.ProbeAction.Calls); // one child run per item

        var loop = parent.Steps.Single(s => s.StepOrder == 0);
        Assert.Equal(ExecutionStatus.Succeeded, loop.Status);
        using var results = JsonDocument.Parse(loop.Output!);
        Assert.Equal(JsonValueKind.Array, results.RootElement.ValueKind);
        Assert.Equal(3, results.RootElement.GetArrayLength());
        Assert.All(
            results.RootElement.EnumerateArray(),
            item => Assert.Equal("Succeeded", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Empty_array_completes_immediately_and_continues()
    {
        fixture.ProbeAction.Reset();
        var childId = await SeedAsync(new StepDefinition("test.probe", "c", "{}"));
        var parentId = await SeedAsync(
            new StepDefinition("forEach", "loop", $$"""{"items":[],"workflowId":"{{childId}}"}"""),
            new StepDefinition("test.probe", "after", "{}"));

        var parent = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, parentId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        Assert.Equal("[]", parent.Steps.Single(s => s.StepOrder == 0).Output);
        Assert.Equal(1, fixture.ProbeAction.Calls); // only "after" ran; the child never did
    }
}
