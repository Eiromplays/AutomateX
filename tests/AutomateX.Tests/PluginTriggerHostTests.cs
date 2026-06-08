using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Connections;
using AutomateX.Modules.Triggers;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

public sealed record EchoConfigTriggerConfig(string Secret);

[Trigger("test.echoConfig", "Echo Config", Description = "Fires once with its resolved config, then parks.")]
public sealed class EchoConfigTrigger : ITriggerListener<EchoConfigTriggerConfig>
{
    public async Task RunAsync(EchoConfigTriggerConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        await context.FireAsync($$"""{"secret":"{{config.Secret}}"}""");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // exactly one fire per start
    }
}

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

    // The rule that makes secret-bearing trigger plugins viable: trigger configs
    // support {{connections.<name>.<field>}}, resolved at listener start — the
    // stored row (and the UI) keep the template, never the secret.
    [Fact]
    public async Task Trigger_configs_resolve_connection_templates_at_listener_start()
    {
        fixture.ProbeAction.Reset();
        var connectionName = $"trigconn-{Guid.CreateVersion7():N}";
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);

        Guid triggerId;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<SecretCipher>();
            dbContext.Connections.Add(Connection.Create(
                connectionName, "test", cipher.Encrypt("""{"token":"trigger-s3cret"}""")));

            var trigger = Trigger.Create(
                workflowId, "test.echoConfig",
                "{\"secret\":\"{{connections." + connectionName + ".token}}\"}",
                null);
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
                    .FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.TriggeredBy == "test.echoConfig");

                if (execution is not null)
                {
                    Assert.Contains("trigger-s3cret", execution.TriggerPayload);
                    return;
                }

                await Task.Delay(200);
            }

            Assert.Fail("the trigger never fired with a resolved config");
        }
        finally
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await dbContext.Triggers
                .Where(x => x.Id == triggerId)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Enabled, false));
        }
    }
}
