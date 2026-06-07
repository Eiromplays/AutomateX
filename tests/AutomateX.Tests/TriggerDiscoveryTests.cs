using AutomateX.Engine.Triggers;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

public sealed record TestTickConfig(int Count);

[Trigger("test.tick", "Test Tick", Description = "Fires Count times, then returns.")]
public sealed class TestTickTrigger : ITriggerListener<TestTickConfig>
{
    public async Task RunAsync(TestTickConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        for (var i = 1; i <= config.Count; i++)
        {
            await context.FireAsync($$"""{"tick":{{i}}}""");
        }
    }
}

public sealed class TriggerDiscoveryTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<AutomateX.Engine.Actions.ActionContextFactory>()
            .BuildServiceProvider();

    [Fact]
    public void Discovers_attributed_triggers_with_config_schema()
    {
        using var services = BuildServices();

        var triggers = TriggerDiscovery.FromAssembly(typeof(TestTickTrigger).Assembly, "test", services).ToList();

        var tick = Assert.Single(triggers, x => x.Descriptor.Type == "test.tick");
        Assert.Equal("Test Tick", tick.Descriptor.DisplayName);
        Assert.Equal("test", tick.Descriptor.Source);
        Assert.NotNull(tick.Descriptor.ConfigSchema);
        Assert.Contains("count", tick.Descriptor.ConfigSchema.ToJsonString());
    }

    [Fact]
    public async Task Runner_deserializes_config_and_fires_payloads()
    {
        using var services = BuildServices();
        var trigger = Assert.Single(
            TriggerDiscovery.FromAssembly(typeof(TestTickTrigger).Assembly, "test", services),
            x => x.Descriptor.Type == "test.tick");

        List<string?> fired = [];
        var runner = trigger.CreateRunner();
        await runner.RunAsync(
            """{"count":2}""",
            new TriggerRunnerContext(Guid.CreateVersion7(), Guid.CreateVersion7(), payload =>
            {
                fired.Add(payload);
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        Assert.Equal(2, fired.Count);
        Assert.Contains("\"tick\":1", fired[0]);
        Assert.Contains("\"tick\":2", fired[1]);
    }
}
