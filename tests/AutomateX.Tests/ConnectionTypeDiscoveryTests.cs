using AutomateX.Engine;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

[ConnectionType("test.service", "Test Service", Description = "A made-up service for discovery tests.")]
public sealed class TestServiceConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("baseUrl", "Base URL", Secret: false, HelpText: "e.g. https://api.test"),
        new("apiKey", "API key", DocsUrl: "https://test/keys"),
        new("note", "Note", Secret: false, Required: false),
    ];
}

public sealed class ConnectionTypeDiscoveryTests
{
    private static ServiceProvider Services() =>
        new ServiceCollection().AddLogging().BuildServiceProvider();

    [Fact]
    public void Discovers_attributed_connection_types_with_fields()
    {
        using var services = Services();

        var types = ConnectionTypeDiscovery.FromAssembly(typeof(TestServiceConnectionType).Assembly, "test", services).ToList();

        var type = Assert.Single(types, x => x.Type == "test.service");
        Assert.Equal("Test Service", type.DisplayName);
        Assert.Equal("test", type.Source);
        Assert.Equal(3, type.Fields.Count);

        var baseUrl = type.Fields[0];
        Assert.Equal("baseUrl", baseUrl.Key);
        Assert.Equal("Base URL", baseUrl.Label);
        Assert.False(baseUrl.Secret);
        Assert.True(baseUrl.Required);

        var apiKey = type.Fields[1];
        Assert.True(apiKey.Secret);
        Assert.Equal("https://test/keys", apiKey.DocsUrl);

        Assert.False(type.Fields[2].Required);
    }

    [Fact]
    public void Registry_exposes_discovered_types()
    {
        using var services = Services();
        var plugins = new PluginAssemblies(
            Options.Create(new EngineOptions { PluginsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }),
            NullLogger<PluginAssemblies>.Instance);

        var registry = new ConnectionTypeRegistry(
            [new AssemblyConnectionTypeSource(typeof(TestServiceConnectionType).Assembly, services)],
            plugins,
            services,
            NullLogger<ConnectionTypeRegistry>.Instance);

        Assert.Contains(registry.Descriptors, x => x.Type == "test.service");
    }
}

internal sealed class AssemblyConnectionTypeSource(System.Reflection.Assembly assembly, IServiceProvider services)
    : IConnectionTypeSource
{
    public IEnumerable<ConnectionTypeDescriptor> GetConnectionTypes() =>
        ConnectionTypeDiscovery.FromAssembly(assembly, "test", services);
}
