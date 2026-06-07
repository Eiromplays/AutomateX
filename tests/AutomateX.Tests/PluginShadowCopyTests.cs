using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// The runtime caches PE images by absolute path, so reloading a replaced DLL from
// its original path can serve stale bytes. Rule: every load shadow-copies the plugin
// to a unique path — the plugins dir is never locked and new bytes always load.
public sealed class PluginShadowCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"automatex-shadow-tests-{Guid.NewGuid():N}");

    public PluginShadowCopyTests()
    {
        // A real managed assembly posing as a plugin (content is irrelevant here —
        // these tests pin loading mechanics, not discovery).
        var pluginDir = Path.Combine(_root, "FakePlugin");
        Directory.CreateDirectory(pluginDir);
        File.Copy(typeof(PluginAssemblies).Assembly.Location, Path.Combine(pluginDir, "FakePlugin.dll"));
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

    private PluginAssemblies Build() => new(
        Options.Create(new EngineOptions { PluginsPath = _root }),
        NullLogger<PluginAssemblies>.Instance);

    [Fact]
    public void Plugins_load_from_a_shadow_copy_not_the_plugins_dir()
    {
        var assemblies = Build();

        var plugin = Assert.Single(assemblies.Current.Global);

        Assert.False(plugin.Assembly.Location.StartsWith(_root, StringComparison.Ordinal),
            $"expected a shadow path, got {plugin.Assembly.Location}");
        Assert.True(File.Exists(plugin.Assembly.Location));
    }

    [Fact]
    public void The_original_plugin_file_stays_replaceable_while_loaded()
    {
        var assemblies = Build();
        _ = assemblies.Current; // force the load

        var original = Path.Combine(_root, "FakePlugin", "FakePlugin.dll");
        File.Delete(original); // would throw if the loader held the original open (Windows semantics)
        File.Copy(typeof(PluginAssemblies).Assembly.Location, original);
    }

    [Fact]
    public void Each_reload_uses_a_fresh_shadow_path()
    {
        var assemblies = Build();
        var first = Assert.Single(assemblies.Current.Global).Assembly.Location;

        assemblies.Reload();
        var second = Assert.Single(assemblies.Current.Global).Assembly.Location;

        Assert.NotEqual(first, second);
    }
}
