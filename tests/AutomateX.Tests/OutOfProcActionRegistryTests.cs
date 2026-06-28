using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: with OutOfProcPlugins on, ActionRegistry discovers a plugin's actions by describing its host
// (no in-host load) and resolves an executor that runs the action in that process. Skips if unbuilt.
public sealed class OutOfProcActionRegistryTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Discovers_and_executes_a_plugin_action_out_of_process()
    {
        var (hostDll, sampleBin) = Locate();
        if (hostDll is null || sampleBin is null)
        {
            output.WriteLine("Skipped — build the solution first.");
            return;
        }

        var pluginsRoot = Path.Combine(Path.GetTempPath(), $"ax-oop-{Guid.CreateVersion7():N}");
        CopyInto(sampleBin, Path.Combine(pluginsRoot, "AutomateX.SamplePlugin"));

        var options = Options.Create(new EngineOptions { OutOfProcPlugins = true, PluginsPath = pluginsRoot });
        var plugins = new PluginAssemblies(options, NullLogger<PluginAssemblies>.Instance);
        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll);

        try
        {
            var registry = new ActionRegistry(
                [], [], plugins, supervisor, NullLogger<ActionRegistry>.Instance);

            Assert.Contains(registry.Descriptors(Workspace.DefaultId), d => d.Type == "sample.echo");

            var executor = registry.Get("sample.echo", Workspace.DefaultId);
            var result = await executor.ExecuteAsync(
                """{"message":"via registry"}""", new ActionInvocation(Guid.Empty, Guid.Empty, 0));

            Assert.Contains("via registry", result);
        }
        finally
        {
            await supervisor.DisposeAsync();
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    private static void CopyInto(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }
    }

    private static (string? HostDll, string? SampleBin) Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutomateX.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return (null, null);
        }

        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        const string tfm = "net10.0";

        var host = Path.Combine(dir.FullName, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var sampleBin = Path.Combine(dir.FullName, "samples", "AutomateX.SamplePlugin", "bin", config, tfm);
        return File.Exists(host) && Directory.Exists(sampleBin) ? (host, sampleBin) : (null, null);
    }
}
