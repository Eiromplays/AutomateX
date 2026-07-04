using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

public sealed class PluginAssembliesDisabledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ax-disabled-{Guid.NewGuid():N}");

    public PluginAssembliesDisabledTests()
    {
        var dir = Path.Combine(_root, "Foo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Foo.dll"), ""); // presence is all EnumeratePaths checks
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Disabled_marker_is_reflected_and_toggles()
    {
        var plugins = new PluginAssemblies(
            Options.Create(new EngineOptions { PluginsPath = _root }), NullLogger<PluginAssemblies>.Instance);

        Assert.False(Assert.Single(plugins.EnumeratePaths()).Disabled);

        Assert.True(plugins.SetDisabled("Foo", workspaceId: null, disabled: true));
        Assert.True(Assert.Single(plugins.EnumeratePaths()).Disabled);

        plugins.SetDisabled("Foo", workspaceId: null, disabled: false);
        Assert.False(Assert.Single(plugins.EnumeratePaths()).Disabled);
    }

    [Fact]
    public void SetDisabled_returns_false_for_unknown_plugin()
    {
        var plugins = new PluginAssemblies(
            Options.Create(new EngineOptions { PluginsPath = _root }), NullLogger<PluginAssemblies>.Instance);

        Assert.False(plugins.SetDisabled("Ghost", workspaceId: null, disabled: true));
    }
}
