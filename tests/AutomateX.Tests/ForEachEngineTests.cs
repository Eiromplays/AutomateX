using System.Text.Json;
using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// forEach maps a child workflow over an array: one child run per item, results collected in item order
// as the step output, the parent resuming once all items are done. v4.10 rebuilt the accumulator on
// per-item rows + atomic counters; these end-to-end cases pin the sequential behaviour across that
// rewrite (concurrency-specific cases live in the parallel slice).
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
    public async Task Gathers_every_item_result_in_order()
    {
        fixture.ProbeAction.Reset();
        // The child echoes its item (the item is the child's trigger payload) so the ordered results
        // array is observable — the finisher gathers the per-item rows ORDER BY ItemIndex.
        var childId = await SeedAsync(new StepDefinition("test.probe", "c", """{"v":"{{trigger.payload}}"}"""));
        var parentId = await SeedAsync(
            new StepDefinition("forEach", "loop", $$"""{"items":["a","b","c","d"],"workflowId":"{{childId}}"}"""));

        var parent = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, parentId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        Assert.Equal(4, fixture.ProbeAction.Calls);

        using var results = JsonDocument.Parse(parent.Steps.Single(s => s.StepOrder == 0).Output!);
        var items = results.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("output").GetString() ?? "")
            .ToList();
        Assert.Equal(4, items.Count);
        Assert.Contains("a", items[0]);
        Assert.Contains("b", items[1]);
        Assert.Contains("c", items[2]);
        Assert.Contains("d", items[3]);
    }

    [Fact]
    public async Task Runs_items_in_parallel_and_still_orders_results()
    {
        fixture.ProbeAction.Reset();
        var childId = await SeedAsync(new StepDefinition("test.probe", "c", """{"v":"{{trigger.payload}}"}"""));
        // 7 items, 3 at a time: children finish in any order, but the finisher gathers by ItemIndex.
        var parentId = await SeedAsync(
            new StepDefinition(
                "forEach", "loop",
                $$"""{"items":["a","b","c","d","e","f","g"],"workflowId":"{{childId}}","concurrency":3}"""));

        var parent = await TestData.WaitForCompletedAsync(
            fixture.Host, await TestData.ExecuteAsync(fixture.Host, parentId), Timeout);

        Assert.Equal(ExecutionStatus.Succeeded, parent.Status);
        Assert.Equal(7, fixture.ProbeAction.Calls); // every item ran exactly once

        using var results = JsonDocument.Parse(parent.Steps.Single(s => s.StepOrder == 0).Output!);
        var items = results.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("output").GetString() ?? "")
            .ToList();
        Assert.Equal(7, items.Count);
        foreach (var (expected, i) in new[] { "a", "b", "c", "d", "e", "f", "g" }.Select((v, i) => (v, i)))
        {
            Assert.Contains(expected, items[i]);
        }
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
