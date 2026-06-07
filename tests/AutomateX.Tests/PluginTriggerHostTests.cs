using AutomateX.Database;
using AutomateX.Modules.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Plugin triggers end-to-end: an enabled trigger row whose type comes from a
// discovered listener gets supervised by the host and fires real executions
// carrying the listener's payload.
public sealed class PluginTriggerHostTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Enabled_plugin_trigger_fires_a_workflow_with_its_payload()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        Guid triggerId;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var trigger = Trigger.Create(workflowId, "test.tick", """{"count":1}""", null);
            dbContext.Triggers.Add(trigger);
            await dbContext.SaveChangesAsync();
            triggerId = trigger.Id;
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var scope = fixture.Host.Services.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
                var execution = await dbContext.Executions.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.TriggeredBy == "test.tick");

                if (execution is not null)
                {
                    Assert.Contains("tick", execution.TriggerPayload);
                    return;
                }

                await Task.Delay(200);
            }

            Assert.Fail("the plugin trigger never fired");
        }
        finally
        {
            // Stop the listener from re-running for the rest of the fixture's life.
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await dbContext.Triggers
                .Where(x => x.Id == triggerId)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Enabled, false));
        }
    }
}
