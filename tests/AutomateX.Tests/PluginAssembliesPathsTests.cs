using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Out-of-proc discovery enumerates plugin dll paths (global + per-workspace) without loading anything,
// following the same plugins/<Name>/<Name>.dll convention as the in-proc loader.
public sealed class PluginAssembliesPathsTests
{
    [Fact]
    public void Enumerates_global_and_workspace_plugin_paths_without_loading()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ax-plugins-{Guid.CreateVersion7():N}");
        var workspaceId = Guid.CreateVersion7();
        try
        {
            TouchPlugin(Path.Combine(root, "Alpha"), "Alpha");
            TouchPlugin(Path.Combine(root, PluginAssemblies.WorkspacesDirName, workspaceId.ToString(), "Beta"), "Beta");
            Directory.CreateDirectory(Path.Combine(root, "Gamma")); // no dll → skipped

            var plugins = new PluginAssemblies(
                Options.Create(new EngineOptions { PluginsPath = root }), NullLogger<PluginAssemblies>.Instance);

            var paths = plugins.EnumeratePaths();

            var alpha = Assert.Single(paths, p => p.Name == "Alpha");
            Assert.Null(alpha.WorkspaceId);
            var beta = Assert.Single(paths, p => p.Name == "Beta");
            Assert.Equal(workspaceId, beta.WorkspaceId);
            Assert.DoesNotContain(paths, p => p.Name == "Gamma");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TouchPlugin(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.dll"), "");
    }
}
