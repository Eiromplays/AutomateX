using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

public sealed record TestEchoConfig(string Message);

public sealed record TestEchoResult(string Message);

[Action("test.echo", "Test Echo", Description = "Discovery test action.")]
public sealed class TestEchoAction : IAction<TestEchoConfig, TestEchoResult>
{
    public Task<TestEchoResult> ExecuteAsync(
        TestEchoConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TestEchoResult($"echo:{config.Message}"));
}

public sealed class ActionDiscoveryTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

    [Fact]
    public async Task Discovers_attributed_actions_with_config_schema()
    {
        await using var services = BuildServices();

        var actions = ActionDiscovery.FromAssembly(typeof(TestEchoAction).Assembly, "test", services).ToList();

        var echo = Assert.Single(actions, x => x.Descriptor.Type == "test.echo");
        Assert.Equal("Test Echo", echo.Descriptor.DisplayName);
        Assert.Equal("test", echo.Descriptor.Source);
        Assert.NotNull(echo.Descriptor.ConfigSchema);
        Assert.Contains("message", echo.Descriptor.ConfigSchema.ToJsonString());
    }

    [Fact]
    public async Task Discovered_executor_roundtrips_json()
    {
        await using var services = BuildServices();

        var actions = ActionDiscovery.FromAssembly(typeof(TestEchoAction).Assembly, "test", services).ToList();
        var executor = Assert.Single(actions, x => x.Descriptor.Type == "test.echo").Executor;

        var output = await executor.ExecuteAsync("""{"message":"hi"}""");

        Assert.NotNull(output);
        Assert.Contains("echo:hi", output);
    }
}
