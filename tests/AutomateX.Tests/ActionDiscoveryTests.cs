using System.Text.Json.Nodes;
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

public sealed record TestMultilineConfig([property: Multiline] string Body, string Subject);

public sealed record TestMultilineResult(string Ok);

[Action("test.multiline", "Test Multiline", Description = "Multiline schema test.")]
public sealed class TestMultilineAction : IAction<TestMultilineConfig, TestMultilineResult>
{
    public Task<TestMultilineResult> ExecuteAsync(
        TestMultilineConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TestMultilineResult("ok"));
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

        var invocation = new ActionInvocation(Guid.CreateVersion7(), Guid.CreateVersion7(), 0);
        var output = await executor.ExecuteAsync("""{"message":"hi"}""", invocation);

        Assert.NotNull(output);
        Assert.Contains("echo:hi", output);
    }

    [Fact]
    public async Task Multiline_attribute_stamps_format_on_the_property_schema()
    {
        await using var services = BuildServices();

        var actions = ActionDiscovery.FromAssembly(typeof(TestMultilineAction).Assembly, "test", services).ToList();
        var probe = Assert.Single(actions, x => x.Descriptor.Type == "test.multiline");

        var properties = JsonNode.Parse(probe.Descriptor.ConfigSchema!.ToJsonString())!["properties"]!;
        // [Multiline] -> format:"multiline" (camelCased by the Web naming policy); plain strings carry no format.
        Assert.Equal("multiline", properties["body"]!["format"]!.GetValue<string>());
        Assert.Null(properties["subject"]!["format"]);
    }
}
