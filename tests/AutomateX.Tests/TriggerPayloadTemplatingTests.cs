using AutomateX.Database;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// End-to-end proof that an inbound trigger payload (webhook body, manual-run body, …) reaches an
// arbitrary step's config via {{trigger.payload.*}}: a string token interpolates inside a string,
// a whole-value token keeps its JSON type (number stays a number).
public sealed class TriggerPayloadTemplatingTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Trigger_payload_interpolates_into_a_step_config()
    {
        fixture.ProbeAction.Reset();

        Guid workflowId;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var workflow = Workflow.Create($"payload-{Guid.CreateVersion7():N}", null);
            workflow.AddVersion([
                new StepDefinition("test.probe", "echo", """{"who":"{{trigger.payload.name}}","n":"{{trigger.payload.count}}"}"""),
            ]);
            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync();
            workflowId = workflow.Id;
        }

        var execution = await TestData.WaitForTerminalAsync(
            fixture.Host,
            await TestData.ExecuteAsync(fixture.Host, workflowId, """{"name":"Eirik","count":3}"""),
            TerminalTimeout);

        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Contains(
            fixture.ProbeAction.ReceivedConfigs,
            c => c.Contains("\"who\":\"Eirik\"", StringComparison.Ordinal)
                && c.Contains("\"n\":3", StringComparison.Ordinal));
    }
}
